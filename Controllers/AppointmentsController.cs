using ClinicAdoNet.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicAdoNet.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    [HttpGet]
    public async Task<IActionResult> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        var appointments = new List<AppointmentListDto>();
        
        await using var connection = new SqlConnection(connectionString);
        
        var query = @"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Status", (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("@PatientLastName", (object?)patientLastName ?? DBNull.Value);
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            appointments.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail"))
            });
        }
        return Ok(appointments);
    }
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentDetails(int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        
        var query = @"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.FirstName + N' ' + d.LastName AS DoctorFullName,
                d.LicenseNumber AS DoctorLicenseNumber
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON a.IdPatient = p.IdPatient
            JOIN dbo.Doctors d ON a.IdDoctor = d.IdDoctor
            WHERE a.IdAppointment = @IdAppointment;";

        await using var command = new SqlCommand(query, connection);
        
        command.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return NotFound($"Wizyta o ID {idAppointment} nie została znaleziona.");
        }

        var appointmentDetails = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) 
                ? string.Empty 
                : reader.GetString(reader.GetOrdinal("InternalNotes")),

            PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            
            DoctorFullName = reader.GetString(reader.GetOrdinal("DoctorFullName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber"))
        };

        return Ok(appointmentDetails);
    }
    
    [HttpPost]
    public async Task<IActionResult> CreateAppointment(CreateAppointmentRequestDto request)
    {
        if (request.AppointmentDate < DateTime.UtcNow)
        {
            return BadRequest("Termin wizyty nie może być w przeszłości.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        {
            return BadRequest("Opis wizyty nie może być pusty i może mieć maksymalnie 250 znaków.");
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var patientQuery = "SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient";
        await using var patientCmd = new SqlCommand(patientQuery, connection);
        patientCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        
        var patientActiveObj = await patientCmd.ExecuteScalarAsync();
        if (patientActiveObj == null) 
            return NotFound("Wskazany pacjent nie istnieje.");
        if (!(bool)patientActiveObj) 
            return BadRequest("Wskazany pacjent jest nieaktywny.");
        
        var doctorQuery = "SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor";
        await using var doctorCmd = new SqlCommand(doctorQuery, connection);
        doctorCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        
        var doctorActiveObj = await doctorCmd.ExecuteScalarAsync();
        if (doctorActiveObj == null) 
            return NotFound("Wskazany lekarz nie istnieje.");
        if (!(bool)doctorActiveObj) 
            return BadRequest("Wskazany lekarz jest nieaktywny.");
        
        var conflictQuery = "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate";
        await using var conflictCmd = new SqlCommand(conflictQuery, connection);
        conflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        conflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        
        var conflictCount = (int)await conflictCmd.ExecuteScalarAsync();
        if (conflictCount > 0)
        {
            return Conflict("Lekarz ma już zaplanowaną wizytę w tym terminie.");
        }
        var insertQuery = @"
            INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
            OUTPUT INSERTED.IdAppointment
            VALUES (@IdPatient, @IdDoctor, @AppointmentDate, 'Scheduled', @Reason);";

        await using var insertCmd = new SqlCommand(insertQuery, connection);
        insertCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        insertCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        insertCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        insertCmd.Parameters.AddWithValue("@Reason", request.Reason);

        var newAppointmentId = (int)await insertCmd.ExecuteScalarAsync();
        return CreatedAtAction(nameof(GetAppointmentDetails), new { idAppointment = newAppointmentId }, null);
    }
    
    [HttpPut("{idAppointment}")]
    public async Task<IActionResult> UpdateAppointment(int idAppointment, UpdateAppointmentRequestDto request)
    {
        var validStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest("Nieprawidłowy status wizyty.");
        }

        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var currentAppQuery = "SELECT Status, AppointmentDate FROM dbo.Appointments WHERE IdAppointment = @IdAppointment";
        await using var currentAppCmd = new SqlCommand(currentAppQuery, connection);
        currentAppCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
        await using var reader = await currentAppCmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return NotFound("Wizyta nie istnieje."); 
        }

        var currentStatus = reader.GetString(reader.GetOrdinal("Status"));
        var currentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate"));
        await reader.CloseAsync();
        
        if (currentStatus == "Completed" && currentDate != request.AppointmentDate)
        {
            return BadRequest("Nie można zmienić terminu zakończonej wizyty.");
        } 
        var checkPersonsQuery = @"
            SELECT 
                (SELECT IsActive FROM dbo.Patients WHERE IdPatient = @IdPatient) AS PatientActive,
                (SELECT IsActive FROM dbo.Doctors WHERE IdDoctor = @IdDoctor) AS DoctorActive;";
        await using var checkPersonsCmd = new SqlCommand(checkPersonsQuery, connection);
        checkPersonsCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        checkPersonsCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        
        await using var personsReader = await checkPersonsCmd.ExecuteReaderAsync();
        await personsReader.ReadAsync();
        
        if (personsReader.IsDBNull(0)) return NotFound("Pacjent nie istnieje.");
        if (!personsReader.GetBoolean(0)) return BadRequest("Pacjent jest nieaktywny.");
        if (personsReader.IsDBNull(1)) return NotFound("Lekarz nie istnieje.");
        if (!personsReader.GetBoolean(1)) return BadRequest("Lekarz jest nieaktywny.");
        await personsReader.CloseAsync();
        if (currentDate != request.AppointmentDate)
        {
            var conflictQuery = "SELECT COUNT(1) FROM dbo.Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @AppointmentDate AND IdAppointment != @IdAppointment";
            await using var conflictCmd = new SqlCommand(conflictQuery, connection);
            conflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
            conflictCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
            conflictCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);

            if ((int)await conflictCmd.ExecuteScalarAsync() > 0)
            {
                return Conflict("Lekarz ma już zaplanowaną inną wizytę w tym terminie.");
            }
        }

        var updateQuery = @"
            UPDATE dbo.Appointments 
            SET IdPatient = @IdPatient, 
                IdDoctor = @IdDoctor, 
                AppointmentDate = @AppointmentDate, 
                Status = @Status, 
                Reason = @Reason, 
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;";

        await using var updateCmd = new SqlCommand(updateQuery, connection);
        updateCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
        updateCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
        updateCmd.Parameters.AddWithValue("@AppointmentDate", request.AppointmentDate);
        updateCmd.Parameters.AddWithValue("@Status", request.Status);
        updateCmd.Parameters.AddWithValue("@Reason", request.Reason);
        updateCmd.Parameters.AddWithValue("@InternalNotes", (object?)request.InternalNotes ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await updateCmd.ExecuteNonQueryAsync(); 
        return Ok(); 
    }
    
    [HttpDelete("{idAppointment}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        var checkQuery = "SELECT Status FROM dbo.Appointments WHERE IdAppointment = @IdAppointment";
        await using var checkCmd = new SqlCommand(checkQuery, connection);
        checkCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
        var statusObj = await checkCmd.ExecuteScalarAsync();
        
        if (statusObj == null)
        {
            return NotFound($"Wizyta o ID {idAppointment} nie istnieje.");
        }

        var status = (string)statusObj;
        if (status == "Completed")
        {
            return Conflict("Nie można usunąć wizyty, która została już zakończona.");
        }
        var deleteQuery = "DELETE FROM dbo.Appointments WHERE IdAppointment = @IdAppointment";
        await using var deleteCmd = new SqlCommand(deleteQuery, connection);
        deleteCmd.Parameters.AddWithValue("@IdAppointment", idAppointment);
        await deleteCmd.ExecuteNonQueryAsync();
        return NoContent();
    }
}