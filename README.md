# ClinicAdoNet API

Projekt backendowy realizujący system obsługi wizyt w przychodni medycznej. Aplikacja została zbudowana w technologii ASP.NET Core Web API z wykorzystaniem bezpośredniej komunikacji z bazą danych przez ADO.NET.

## Charakterystyka projektu
- Wykorzystanie Microsoft.Data.SqlClient zamiast systemów ORM.
- Implementacja wzorca DTO (Data Transfer Object) dla wszystkich punktów końcowych.
- Pełna parametryzacja zapytań SQL w celu ochrony przed atakami SQL Injection.
- Asynchroniczna obsługa żądań i operacji bazodanowych.

## Funkcjonalności
- Pobieranie listy wizyt z filtrowaniem według statusu i nazwiska pacjenta.
- Pobieranie szczegółowych danych konkretnej wizyty (JOIN na tabelach Patients i Doctors).
- Dodawanie nowych wizyt z walidacją dostępności lekarza i aktywności pacjenta.
- Aktualizacja istniejących rekordów z uwzględnieniem reguł biznesowych.
- Usuwanie planowanych wizyt z blokadą usuwania wizyt zakończonych.

## Wymagania i uruchomienie
1. Serwer SQL Server (lokalny lub Docker).
2. Inicjalizacja bazy danych za pomocą dołączonego skryptu SQL.
3. Konfiguracja parametrów połączenia w pliku appsettings.json.
4. Uruchomienie aplikacji poprzez polecenie: dotnet run
