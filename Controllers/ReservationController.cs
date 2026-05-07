// ReservationController.cs - TELJES MŰKÖDŐ VERZIÓ (ZÁRT KÖRŰ FOGLALÁSSAL)
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

[Route("[controller]")]
[ApiController]
public class ReservationController : ControllerBase
{
    private readonly string? _connectionString;
    private readonly ILogger<ReservationController> _logger;

    public ReservationController(IConfiguration configuration, ILogger<ReservationController> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _logger = logger;
    }

    // ========== FOGLALT IDŐPONTOK LEKÉRDEZÉSE (ZÁRT NAPOK FIGYELEMBEVÉTELÉVEL) ==========

    [HttpGet("GetReservationsByDate")]
    public async Task<IActionResult> GetReservationsByDate([FromQuery] string date, [FromQuery] string? tableNumber = null)
    {
        try
        {
           // _logger.LogInformation($"Foglalások lekérdezése dátum szerint: {date}, Asztal: {tableNumber}");

            if (string.IsNullOrEmpty(date))
            {
                return BadRequest(new { success = false, message = "Hiányzó dátum paraméter" });
            }

            // ELLENŐRZÉS: Zárt nap-e?
            bool isDayClosed = await IsDayClosedAsync(date);
            if (isDayClosed)
            {
                _logger.LogWarning($"⛔ Dátum zárt: {date} - nincsenek időpontok elérhetőek");
                return Ok(new 
                { 
                    success = true, 
                    reservations = new List<object>(),
                    count = 0,
                    tableNumber = tableNumber,
                    date = date,
                    isDayClosed = true,
                    closedReason = await GetClosedDayReasonAsync(date),
                    message = "Ez a nap zárt körű rendezvény miatt nem elérhető"
                });
            }

            using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            
            // Ha meg van adva asztalszám, akkor azt is ellenőrizzük
            if (!string.IsNullOrEmpty(tableNumber))
            {
                command.CommandText = @"
                    SELECT Id, Time, TableNumber, Status 
                    FROM Reservations 
                    WHERE Date = @Date 
                    AND Status IN ('active', 'approved', 'confirmed')
                    AND TableNumber = @TableNumber
                    ORDER BY Time ASC";
                
                command.Parameters.AddWithValue("@TableNumber", tableNumber);
            }
            else
            {
                command.CommandText = @"
                    SELECT Id, Time, TableNumber, Status 
                    FROM Reservations 
                    WHERE Date = @Date 
                    AND Status IN ('active', 'approved', 'confirmed')
                    ORDER BY Time ASC";
            }

            command.Parameters.AddWithValue("@Date", date);

            var reservations = new List<object>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var dbTime = !reader.IsDBNull(reader.GetOrdinal("Time")) ? 
                               reader.GetString(reader.GetOrdinal("Time")) : "";
                    
                    // FRONTEND COMPATIBLE TIME FORMAT
                    string frontendTimeSlot = ConvertToFrontendTimeFormat(dbTime);
                    
                    reservations.Add(new
                    {
                        Id = reader.GetInt32(reader.GetOrdinal("Id")),
                        Time = frontendTimeSlot,  // Frontend formátum: "14:00-16:00"
                        TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                     reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                        Status = !reader.IsDBNull(reader.GetOrdinal("Status")) ? 
                                reader.GetString(reader.GetOrdinal("Status")) : "",
                        DbTime = dbTime, // For debugging: "14:00"
                        FrontendTime = frontendTimeSlot // For debugging
                    });
                }
            }

            //_logger.LogInformation($"Foglalások megtalálva: {reservations.Count} db (asztal: {tableNumber})");
            
            foreach (var res in reservations)
            {
                var dynamicRes = res as dynamic;
                _logger.LogInformation($"  - {dynamicRes?.FrontendTime} (DB: {dynamicRes?.DbTime}) asztal #{dynamicRes?.TableNumber} státusz: {dynamicRes?.Status}");
            }

            return Ok(new 
            { 
                success = true, 
                reservations = reservations,
                count = reservations.Count,
                tableNumber = tableNumber,
                date = date,
                isDayClosed = false,
                message = $"{reservations.Count} foglalás található"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Hiba a foglalások lekérdezésekor dátum szerint: {date}, asztal: {tableNumber}");
            return StatusCode(500, new { 
                success = false, 
                message = "Hiba a foglalások lekérdezésekor",
                error = ex.Message 
            });
        }
    }

    [HttpPost("CheckTimeSlot")]
    public async Task<IActionResult> CheckTimeSlot([FromBody] TimeSlotCheckModel model)
    {
        try
        {
            _logger.LogInformation($"Időpont ellenőrzése: Dátum={model.Date}, Időpont={model.TimeSlot}, Asztal={model.TableNumber}");

            if (string.IsNullOrEmpty(model.Date) || string.IsNullOrEmpty(model.TimeSlot))
            {
                return BadRequest(new { 
                    success = false, 
                    message = "Hiányzó dátum vagy időpont paraméter" 
                });
            }

            // ELLENŐRZÉS: Zárt nap-e?
            bool isDayClosed = await IsDayClosedAsync(model.Date);
            if (isDayClosed)
            {
                _logger.LogWarning($"⛔ Időpont ellenőrzés: Dátum zárt: {model.Date}");
                return Ok(new { 
                    success = true, 
                    isReserved = true, // Zárt napon minden időpont "foglalt"
                    isDayClosed = true,
                    closedReason = await GetClosedDayReasonAsync(model.Date),
                    count = 999, // Speciális érték zárt naphoz
                    tableNumber = model.TableNumber,
                    originalTimeSlot = model.TimeSlot,
                    dbTime = ConvertToDbTimeFormat(model.TimeSlot),
                    message = $"Ez a nap zárt körű rendezvény miatt nem elérhető: {await GetClosedDayReasonAsync(model.Date)}"
                });
            }

            // CONVERT FRONTEND TIME TO DATABASE FORMAT
            string dbTime = ConvertToDbTimeFormat(model.TimeSlot);
            _logger.LogInformation($"Időpont konvertálva: '{model.TimeSlot}' -> '{dbTime}'");

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            
            // Table specific check if table number provided
            if (!string.IsNullOrEmpty(model.TableNumber))
            {
                command.CommandText = @"
                    SELECT COUNT(*) as Count 
                    FROM Reservations 
                    WHERE Date = @Date 
                    AND Time = @Time
                    AND Status IN ('active', 'approved', 'confirmed')
                    AND TableNumber = @TableNumber";
                
                command.Parameters.AddWithValue("@TableNumber", model.TableNumber);
            }
            else
            {
                // General check (any table)
                command.CommandText = @"
                    SELECT COUNT(*) as Count 
                    FROM Reservations 
                    WHERE Date = @Date 
                    AND Time = @Time
                    AND Status IN ('active', 'approved', 'confirmed')";
            }

            command.Parameters.AddWithValue("@Date", model.Date);
            command.Parameters.AddWithValue("@Time", dbTime);

            var count = Convert.ToInt64(await command.ExecuteScalarAsync());
            var isReserved = count > 0;

            _logger.LogInformation($"Időpont ellenőrzés eredménye: {(isReserved ? "FOGLALT" : "SZABAD")} (asztal: {model.TableNumber}, db idő: {dbTime})");

            return Ok(new { 
                success = true, 
                isReserved = isReserved,
                isDayClosed = false,
                count = count,
                tableNumber = model.TableNumber,
                originalTimeSlot = model.TimeSlot,
                dbTime = dbTime,
                message = isReserved ? $"Időpont foglalt: {model.TimeSlot}" : $"Időpont szabad: {model.TimeSlot}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Hiba az időpont ellenőrzésekor: {model.Date} {model.TimeSlot} {model.TableNumber}");
            return StatusCode(500, new { 
                success = false, 
                message = "Hiba az időpont ellenőrzésekor",
                error = ex.Message 
            });
        }
    }

    // ========== ZÁRT KÖRŰ FOGLALÁS KEZELÉSE ==========

    [HttpPost("CloseDayForReservations")]
    public async Task<IActionResult> CloseDayForReservations([FromBody] CloseDayModel model)
    {
        try
        {
            _logger.LogInformation($"Zárt nap beállítása kérés: Dátum={model.Date}, Indok={model.Reason}");

            // Ellenőrizzük, hogy admin-e
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Csak admin felhasználók zárhatnak le napot" });
            }

            if (string.IsNullOrEmpty(model.Date))
            {
                return BadRequest(new { success = false, message = "Hiányzó dátum paraméter" });
            }

            // Ellenőrizzük, hogy a dátum nem múltbeli-e
            if (DateTime.TryParse(model.Date, out DateTime closeDate))
            {
                var today = DateTime.Today;
                if (closeDate < today)
                {
                    return BadRequest(new { 
                        success = false, 
                        message = "Múltbeli dátumot nem lehet lezárni",
                        date = model.Date,
                        today = today.ToString("yyyy-MM-dd")
                    });
                }

                // Ellenőrizzük, hogy nem túl távoli jövőbeni dátum-e (max 1 év)
                var maxDate = today.AddYears(1);
                if (closeDate > maxDate)
                {
                    return BadRequest(new { 
                        success = false, 
                        message = "Túl távoli jövőbeli dátum, maximum 1 évre előre lehet lezárni",
                        date = model.Date,
                        maxAllowed = maxDate.ToString("yyyy-MM-dd")
                    });
                }
            }

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1. Ellenőrizzük, hogy van-e már aktív foglalás erre a napra
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = @"
                SELECT COUNT(*) as Count, 
                       GROUP_CONCAT(ReservationId) as ReservationIds
                FROM Reservations 
                WHERE Date = @Date 
                AND Status IN ('active', 'approved', 'confirmed')";

            checkCommand.Parameters.AddWithValue("@Date", model.Date);

            await using (var reader = await checkCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    int activeReservations = reader.GetInt32(0);
                    string? reservationIds = !reader.IsDBNull(1) ? reader.GetString(1) : null;

                    if (activeReservations > 0)
                    {
                        _logger.LogWarning($"⚠️ Zárt nap beállítása meghiúsult: {activeReservations} aktív foglalás van erre a napra");

                        // Vágjuk le a foglalás ID-kat, ha túl hosszúak
                        string shortIds = reservationIds?.Length > 200 ? 
                            reservationIds.Substring(0, 200) + "..." : 
                            reservationIds ?? "N/A";

                        return BadRequest(new
                        {
                            success = false,
                            message = $"Nem lehet lezárni ezt a napot, mert {activeReservations} aktív foglalás van erre a napra.",
                            activeReservations = activeReservations,
                            sampleReservationIds = shortIds,
                            date = model.Date,
                            actionRequired = "Először töröld vagy módosítsd a foglalásokat, majd próbáld újra."
                        });
                    }
                }
            }

            // 2. Ellenőrizzük, hogy ez a dátum már le van-e zárva
            var checkClosedCommand = connection.CreateCommand();
            checkClosedCommand.CommandText = @"
                SELECT COUNT(*) as Count 
                FROM ClosedDays 
                WHERE Date = @Date 
                AND IsActive = 1";

            checkClosedCommand.Parameters.AddWithValue("@Date", model.Date);

            var alreadyClosed = Convert.ToInt64(await checkClosedCommand.ExecuteScalarAsync()) > 0;
            if (alreadyClosed)
            {
                _logger.LogInformation($"ℹ️ Dátum már le van zárva: {model.Date}");
                return Ok(new
                {
                    success = true,
                    message = "Ez a dátum már le van zárva",
                    date = model.Date,
                    alreadyClosed = true
                });
            }

            // 3. Zárd nap bejegyzés létrehozása
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ClosedDays (Date, Reason, ClosedBy, IsActive, CreatedAt, UpdatedAt)
                VALUES (@Date, @Reason, @ClosedBy, 1, datetime('now'), datetime('now'))";

            command.Parameters.AddWithValue("@Date", model.Date);
            command.Parameters.AddWithValue("@Reason", model.Reason ?? "Zárt körű rendezvény");
            command.Parameters.AddWithValue("@ClosedBy", await GetUserNameFromSessionAsync(sessionId) ?? "admin");

            await command.ExecuteNonQueryAsync();

            _logger.LogInformation($"✅ Nap sikeresen lezárva: {model.Date}, Indok: {model.Reason}");

            // 4. Értesítsük a meglévő foglalásokat (ha vannak jövőbeli státuszúak)
            await NotifyFutureReservationsAsync(connection, model.Date, model.Reason);

            return Ok(new
            {
                success = true,
                message = $"Nap sikeresen lezárva: {model.Date}",
                date = model.Date,
                reason = model.Reason,
                closedBy = await GetUserNameFromSessionAsync(sessionId) ?? "admin",
                closedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                notification = "A meglévő jövőbeli foglalások értesítve lettek."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Hiba a nap lezárásakor: {model.Date}");
            return StatusCode(500, new
            {
                success = false,
                message = "Hiba a nap lezárása során",
                error = ex.Message,
                date = model.Date
            });
        }
    }

    [HttpPost("OpenDayForReservations")]
    public async Task<IActionResult> OpenDayForReservations([FromBody] CloseDayModel model)
    {
        try
        {
            _logger.LogInformation($"Zárt nap újranyitása kérés: Dátum={model.Date}");

            // Ellenőrizzük, hogy admin-e
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Csak admin felhasználók nyithatnak újra napot" });
            }

            if (string.IsNullOrEmpty(model.Date))
            {
                return BadRequest(new { success = false, message = "Hiányzó dátum paraméter" });
            }

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1. Ellenőrizzük, hogy le van-e zárva ez a dátum
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = @"
                SELECT Id, Reason, ClosedBy, CreatedAt 
                FROM ClosedDays 
                WHERE Date = @Date 
                AND IsActive = 1";

            checkCommand.Parameters.AddWithValue("@Date", model.Date);

            bool isCurrentlyClosed = false;
            string? closedReason = null;
            string? closedBy = null;
            string? closedAt = null;
            int closedDayId = 0;

            await using (var reader = await checkCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    isCurrentlyClosed = true;
                    closedDayId = reader.GetInt32(0);
                    closedReason = !reader.IsDBNull(1) ? reader.GetString(1) : null;
                    closedBy = !reader.IsDBNull(2) ? reader.GetString(2) : null;
                    closedAt = !reader.IsDBNull(3) ? reader.GetString(3) : null;
                }
            }

            if (!isCurrentlyClosed)
            {
                _logger.LogInformation($"ℹ️ Dátum nem volt lezárva: {model.Date}");
                return Ok(new
                {
                    success = true,
                    message = "Ez a dátum nem volt lezárva",
                    date = model.Date,
                    wasClosed = false
                });
            }

            // 2. Inaktiváljuk a zárt nap bejegyzést
            var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = @"
                UPDATE ClosedDays 
                SET IsActive = 0,
                    UpdatedAt = NOW(),
                    ReopenedAt = NOW(),
                    ReopenedBy = @ReopenedBy
                WHERE Id = @Id";

            updateCommand.Parameters.AddWithValue("@Id", closedDayId);
            updateCommand.Parameters.AddWithValue("@ReopenedBy", await GetUserNameFromSessionAsync(sessionId) ?? "admin");

            var affectedRows = await updateCommand.ExecuteNonQueryAsync();

            if (affectedRows > 0)
            {
                _logger.LogInformation($"✅ Nap sikeresen újranyitva: {model.Date}");

                return Ok(new
                {
                    success = true,
                    message = $"Nap sikeresen újranyitva: {model.Date}",
                    date = model.Date,
                    wasClosed = true,
                    closedReason = closedReason,
                    closedBy = closedBy,
                    closedAt = closedAt,
                    reopenedBy = await GetUserNameFromSessionAsync(sessionId) ?? "admin",
                    reopenedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            else
            {
                _logger.LogWarning($"⚠️ Nem sikerült újranyitni a napot: {model.Date}");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Nem sikerült újranyitni a napot",
                    date = model.Date
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Hiba a nap újranyitásakor: {model.Date}");
            return StatusCode(500, new
            {
                success = false,
                message = "Hiba a nap újranyitása során",
                error = ex.Message,
                date = model.Date
            });
        }
    }

    [HttpGet("GetClosedDays")]
    public async Task<IActionResult> GetClosedDays([FromQuery] string? startDate = null, [FromQuery] string? endDate = null)
    {
        try
        {
            _logger.LogInformation($"Zárt napok lekérdezése: Start={startDate}, End={endDate}");

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            
            if (!string.IsNullOrEmpty(startDate) && !string.IsNullOrEmpty(endDate))
            {
                command.CommandText = @"
                    SELECT Id, Date, Reason, ClosedBy, IsActive, CreatedAt, UpdatedAt, ReopenedAt, ReopenedBy
                    FROM ClosedDays 
                    WHERE Date BETWEEN @StartDate AND @EndDate
                    ORDER BY Date DESC";
                
                command.Parameters.AddWithValue("@StartDate", startDate);
                command.Parameters.AddWithValue("@EndDate", endDate);
            }
            else
            {
                command.CommandText = @"
                    SELECT Id, Date, Reason, ClosedBy, IsActive, CreatedAt, UpdatedAt, ReopenedAt, ReopenedBy
                    FROM ClosedDays 
                    WHERE IsActive = 1
                    ORDER BY Date DESC";
            }

            var closedDays = new List<object>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                closedDays.Add(new
                {
                    Id = reader.GetInt32(0),
                    Date = reader.GetDateTime(1).ToString("yyyy-MM-dd"), 
                    Reason = !reader.IsDBNull(2) ? reader.GetString(2) : null,
                    ClosedBy = !reader.IsDBNull(3) ? reader.GetString(3) : null,
                    IsActive = reader.GetInt32(4) == 1,
                    CreatedAt = reader.GetDateTime(5).ToString("yyyy-MM-dd HH:mm:ss"), 
                    UpdatedAt = !reader.IsDBNull(6) ? reader.GetDateTime(6).ToString("yyyy-MM-dd HH:mm:ss") : null, 
                    ReopenedAt = !reader.IsDBNull(7) ? reader.GetDateTime(7).ToString("yyyy-MM-dd HH:mm:ss") : null, 
                    ReopenedBy = !reader.IsDBNull(8) ? reader.GetString(8) : null
                });
                }
            }

            _logger.LogInformation($"Zárt napok megtalálva: {closedDays.Count} db");

            return Ok(new
            {
                success = true,
                closedDays = closedDays,
                count = closedDays.Count,
                message = $"{closedDays.Count} zárt nap található"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a zárt napok lekérdezésekor");
            return StatusCode(500, new
            {
                success = false,
                message = "Hiba a zárt napok lekérdezésekor",
                error = ex.Message
            });
        }
    }

    [HttpGet("CheckIfDayClosed")]
    public async Task<IActionResult> CheckIfDayClosed([FromQuery] string date)
    {
        try
        {
            if (string.IsNullOrEmpty(date))
            {
                return BadRequest(new { success = false, message = "Hiányzó dátum paraméter" });
            }

            bool isClosed = await IsDayClosedAsync(date);
            string? reason = isClosed ? await GetClosedDayReasonAsync(date) : null;

            _logger.LogInformation($"Zárt nap ellenőrzés: {date} -> {(isClosed ? "ZÁRT" : "NYITVA")}");

            return Ok(new
            {
                success = true,
                date = date,
                isClosed = isClosed,
                reason = reason,
                message = isClosed ? $"Nap zárt: {reason}" : "Nap nyitva"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Hiba a zárt nap ellenőrzésekor: {date}");
            return StatusCode(500, new
            {
                success = false,
                message = "Hiba a zárt nap ellenőrzésekor",
                error = ex.Message,
                date = date
            });
        }
    }

    // ========== MEGLÉVŐ ENDPOINT-OK (ZÁRT NAPOKAT FIGYELEMBEVÉVE) ==========

[HttpGet("GetUserReservations")]
public async Task<IActionResult> GetUserReservations()
{
    try
    {
        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        var userName = await GetUserNameFromSessionAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Érvénytelen session" });
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.*, 
                   u.Email as UserEmail
            FROM Reservations r
            LEFT JOIN User u ON r.UserId = u.UserName
            WHERE r.UserId = @UserId 
            ORDER BY r.Date DESC, r.Time DESC";

        command.Parameters.AddWithValue("@UserId", userName);

        var reservations = new List<object>();
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var dbTime = !reader.IsDBNull(reader.GetOrdinal("Time")) ? 
                           reader.GetString(reader.GetOrdinal("Time")) : "";
                
                reservations.Add(new
                {
                    ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                    UserId = reader.GetString(reader.GetOrdinal("UserId")),
                    UserName = reader.GetString(reader.GetOrdinal("UserId")),
                    UserEmail = !reader.IsDBNull(reader.GetOrdinal("UserEmail")) ? 
                               reader.GetString(reader.GetOrdinal("UserEmail")) : null,
                    TableName = reader.GetString(reader.GetOrdinal("TableName")),
                    TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                 reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                    TableLocation = !reader.IsDBNull(reader.GetOrdinal("TableLocation")) ? 
                                  reader.GetString(reader.GetOrdinal("TableLocation")) : null,
                    Date = reader.GetString(reader.GetOrdinal("Date")),
                    Time = dbTime,
                    FrontendTime = ConvertToFrontendTimeFormat(dbTime),
                    Guests = reader.GetInt32(reader.GetOrdinal("Guests")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    OrderId = !reader.IsDBNull(reader.GetOrdinal("OrderId")) ? 
                             reader.GetString(reader.GetOrdinal("OrderId")) : null,
                    Message = !reader.IsDBNull(reader.GetOrdinal("Message")) ? 
                             reader.GetString(reader.GetOrdinal("Message")) : null,
                    ExtraServices = !reader.IsDBNull(reader.GetOrdinal("ExtraServices")) ? 
                                   reader.GetString(reader.GetOrdinal("ExtraServices")) : null,
                    CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                            reader.GetString(reader.GetOrdinal("UpdatedAt")) : null
                });
            }
        }

        return Ok(new
        {
            success = true,
            count = reservations.Count,
            reservations = reservations,
            message = $"{reservations.Count} foglalás található"
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a felhasználó foglalásainak lekérdezésekor");
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba az adatok lekérdezésekor",
            error = ex.Message 
        });
    }
}
private string SafeGetString(MySqlDataReader reader, string columnName)
{
    try
    {
        var idx = reader.GetOrdinal(columnName);
        if (reader.IsDBNull(idx))
            return null;
        
        var value = reader.GetValue(idx);
        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        
        return value.ToString();
    }
    catch
    {
        return null;
    }
}
[HttpGet("GetAllReservations")]
public async Task<IActionResult> GetAllReservations()
{
    try
    {
        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        var isAdmin = await IsUserAdminAsync(sessionId);
        if (!isAdmin)
        {
            return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.*, 
                   u.Email as UserEmail
            FROM Reservations r
            LEFT JOIN User u ON r.UserId = u.UserName
            ORDER BY r.Date DESC, r.Time DESC";

        var reservations = new List<object>();
        using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var dbTime = !reader.IsDBNull(reader.GetOrdinal("Time")) ? 
                           reader.GetString(reader.GetOrdinal("Time")) : "";
                
                reservations.Add(new
                {
                    ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                    UserId = reader.GetString(reader.GetOrdinal("UserId")),
                    UserName = reader.GetString(reader.GetOrdinal("UserId")),
                    UserEmail = !reader.IsDBNull(reader.GetOrdinal("UserEmail")) ? 
                               reader.GetString(reader.GetOrdinal("UserEmail")) : null,
                    TableName = reader.GetString(reader.GetOrdinal("TableName")),
                    TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                 reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                    TableLocation = !reader.IsDBNull(reader.GetOrdinal("TableLocation")) ? 
                                  reader.GetString(reader.GetOrdinal("TableLocation")) : null,
                    Date = reader.GetString(reader.GetOrdinal("Date")),
                    Time = dbTime,
                    FrontendTime = ConvertToFrontendTimeFormat(dbTime),
                    Guests = reader.GetInt32(reader.GetOrdinal("Guests")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    OrderId = !reader.IsDBNull(reader.GetOrdinal("OrderId")) ? 
                             reader.GetString(reader.GetOrdinal("OrderId")) : null,
                    Message = !reader.IsDBNull(reader.GetOrdinal("Message")) ? 
                             reader.GetString(reader.GetOrdinal("Message")) : null,
                    ExtraServices = !reader.IsDBNull(reader.GetOrdinal("ExtraServices")) ? 
                                   reader.GetString(reader.GetOrdinal("ExtraServices")) : null,
                    CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                            reader.GetString(reader.GetOrdinal("UpdatedAt")) : null
                });
            }
        }

        return Ok(new
        {
            success = true,
            count = reservations.Count,
            reservations = reservations,
            message = $"{reservations.Count} foglalás található"
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba az összes foglalás lekérdezésekor");
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba az adatok lekérdezésekor",
            error = ex.Message 
        });
    }
}

    [HttpPost("Approve")]
    public async Task<IActionResult> ApproveReservation([FromBody] ReservationActionModel model)
    {
        try
        {
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            // Ellenőrizzük, hogy admin-e
            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
            }

            if (string.IsNullOrEmpty(model.ReservationId))
            {
                return BadRequest(new { success = false, message = "Hiányzó foglalás azonosító" });
            }

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Reservations 
                SET Status = 'approved',
                    UpdatedAt = UpdatedAt = NOW()
                WHERE ReservationId = @ReservationId";
            command.Parameters.AddWithValue("@ReservationId", model.ReservationId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Foglalás elfogadva: {ReservationId}", model.ReservationId);
                return Ok(new { success = true, message = "Foglalás sikeresen elfogadva" });
            }
            else
            {
                return NotFound(new { success = false, message = "Foglalás nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a foglalás elfogadásakor");
            return StatusCode(500, new { success = false, message = "Hiba a foglalás elfogadása során" });
        }
    }

    [HttpPost("Reject")]
    public async Task<IActionResult> RejectReservation([FromBody] ReservationActionModel model)
    {
        try
        {
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            // Ellenőrizzük, hogy admin-e
            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
            }

            if (string.IsNullOrEmpty(model.ReservationId))
            {
                return BadRequest(new { success = false, message = "Hiányzó foglalás azonosító" });
            }

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE Reservations 
                SET Status = 'cancelled',
                    UpdatedAt = UpdatedAt = NOW()
                WHERE ReservationId = @ReservationId";
            command.Parameters.AddWithValue("@ReservationId", model.ReservationId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Foglalás elutasítva: {ReservationId}", model.ReservationId);
                return Ok(new { success = true, message = "Foglalás sikeresen elutasítva" });
            }
            else
            {
                return NotFound(new { success = false, message = "Foglalás nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a foglalás elutasításakor");
            return StatusCode(500, new { success = false, message = "Hiba a foglalás elutasítása során" });
        }
    }

[HttpPost("UpdateReservationStatus")]
public async Task<IActionResult> UpdateReservationStatus([FromBody] UpdateReservationStatusModel model)
{
    try
    {
        _logger.LogInformation("Asztalfoglalás státusz frissítése kérés érkezett: {@Model}", model);

        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        // Felhasználónév lekérése
        var userName = await GetUserNameFromSessionAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Érvénytelen session" });
        }

        if (string.IsNullOrEmpty(model.ReservationId))
        {
            return BadRequest(new { success = false, message = "Hiányzó foglalás azonosító" });
        }

        if (string.IsNullOrEmpty(model.Status))
        {
            return BadRequest(new { success = false, message = "Hiányzó státusz" });
        }

        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Ellenőrizzük, hogy a felhasználóé-e a foglalás
        var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = @"
            SELECT UserId, Status, TableName, Date, Time, UpdatedAt, ExtraServices
            FROM Reservations 
            WHERE ReservationId = @ReservationId";
        checkCommand.Parameters.AddWithValue("@ReservationId", model.ReservationId);

        string? reservationUserId = null;
        string? currentStatus = null;
        string? tableName = null;
        string? date = null;
        string? time = null;
        string? currentUpdatedAt = null;
        string? currentExtraServices = null;
        
        using (var reader = await checkCommand.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                reservationUserId = reader.GetString(reader.GetOrdinal("UserId"));
                currentStatus = reader.GetString(reader.GetOrdinal("Status"));
                tableName = reader.GetString(reader.GetOrdinal("TableName"));
                date = reader.GetString(reader.GetOrdinal("Date"));
                time = reader.GetString(reader.GetOrdinal("Time"));
                currentUpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                                  reader.GetString(reader.GetOrdinal("UpdatedAt")) : null;
                currentExtraServices = !reader.IsDBNull(reader.GetOrdinal("ExtraServices")) ? 
                                      reader.GetString(reader.GetOrdinal("ExtraServices")) : null;
                
                _logger.LogInformation($"Jelenlegi ExtraServices az adatbázisban: {currentExtraServices ?? "NULL"}");
            }
        }
        
        if (reservationUserId == null)
        {
            return NotFound(new { 
                success = false, 
                message = "Foglalás nem található",
                reservationId = model.ReservationId
            });
        }
        
        // Ellenőrizzük, hogy a felhasználó saját foglalását frissíti-e
        if (reservationUserId != userName)
        {
            // Admin ellenőrzése
            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { 
                    success = false, 
                    message = "Nincs jogosultság más felhasználó foglalásának módosításához" 
                });
            }
        }

        // Státusz frissítése - JAVÍTOTT: ExtraServices frissítése is, ha a kérés tartalmazza
        var updateCommand = connection.CreateCommand();
        
        // **Módosítás: Dinamikus SQL összeállítása**
        var sqlParts = new List<string>();
        var parameters = new Dictionary<string, object>();
        
        sqlParts.Add("Status = @Status");
        parameters.Add("@Status", model.Status);
        
        if (!string.IsNullOrEmpty(model.OrderId))
        {
            sqlParts.Add("OrderId = @OrderId");
            parameters.Add("@OrderId", model.OrderId);
        }
        
        // **FONTOS: Ha van ExtraServices a modellben (dinamikus property), akkor frissítjük**
        // Reflection használata a dinamikus property ellenőrzéséhez
        var modelType = model.GetType();
        var extraServicesProperty = modelType.GetProperty("ExtraServices");
        
        if (extraServicesProperty != null)
        {
            var extraServicesValue = extraServicesProperty.GetValue(model) as string;
            if (!string.IsNullOrEmpty(extraServicesValue))
            {
                sqlParts.Add("ExtraServices = @ExtraServices");
                parameters.Add("@ExtraServices", extraServicesValue);
                _logger.LogInformation($"ExtraServices frissítése: {extraServicesValue}");
            }
        }
        
        sqlParts.Add("UpdatedAt = UpdatedAt = NOW()");
        
        var updateSql = $"UPDATE Reservations SET {string.Join(", ", sqlParts)} WHERE ReservationId = @ReservationId";
        parameters.Add("@ReservationId", model.ReservationId);
        
        updateCommand.CommandText = updateSql;
        foreach (var param in parameters)
        {
            updateCommand.Parameters.AddWithValue(param.Key, param.Value);
        }

        var affectedRows = await updateCommand.ExecuteNonQueryAsync();
        
        if (affectedRows > 0)
        {
            _logger.LogInformation(
                "Foglalás státusza frissítve: {ReservationId} ({OldStatus} -> {NewStatus}) " +
                "OrderId: {OrderId}, User: {UserName}, Table: {TableName} {Date} {Time}, " +
                "UpdatedAt: {UpdatedAt} -> {NewUpdatedAt}", 
                model.ReservationId, 
                currentStatus, 
                model.Status, 
                model.OrderId ?? "N/A",
                userName,
                tableName,
                date,
                time,
                currentUpdatedAt ?? "null",
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            
            // Visszaadjuk a frissített foglalás adatait
            var getCommand = connection.CreateCommand();
            getCommand.CommandText = @"
                SELECT r.*, 
                       o.OrderId as LinkedOrderId,
                       o.TotalPrice as OrderTotal,
                       o.Status as OrderStatus
                FROM Reservations r
                LEFT JOIN Orders o ON r.OrderId = o.OrderId
                WHERE r.ReservationId = @ReservationId";
            getCommand.Parameters.AddWithValue("@ReservationId", model.ReservationId);
            
            using (var reader = await getCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var dbTime = !reader.IsDBNull(reader.GetOrdinal("Time")) ? 
                               reader.GetString(reader.GetOrdinal("Time")) : "";
                    
                    var reservationData = new
                    {
                        ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                        UserId = reader.GetString(reader.GetOrdinal("UserId")),
                        TableName = reader.GetString(reader.GetOrdinal("TableName")),
                        TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                     reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                        TableLocation = !reader.IsDBNull(reader.GetOrdinal("TableLocation")) ? 
                                      reader.GetString(reader.GetOrdinal("TableLocation")) : null,
                        Date = reader.GetString(reader.GetOrdinal("Date")),
                        Time = dbTime,
                        FrontendTime = ConvertToFrontendTimeFormat(dbTime),
                        Guests = reader.GetInt32(reader.GetOrdinal("Guests")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        OrderId = !reader.IsDBNull(reader.GetOrdinal("OrderId")) ? 
                                 reader.GetString(reader.GetOrdinal("OrderId")) : null,
                        LinkedOrderId = !reader.IsDBNull(reader.GetOrdinal("LinkedOrderId")) ? 
                                      reader.GetString(reader.GetOrdinal("LinkedOrderId")) : null,
                        OrderTotal = !reader.IsDBNull(reader.GetOrdinal("OrderTotal")) ? 
                                   reader.GetInt64(reader.GetOrdinal("OrderTotal")) : (long?)null,
                        OrderStatus = !reader.IsDBNull(reader.GetOrdinal("OrderStatus")) ? 
                                    reader.GetString(reader.GetOrdinal("OrderStatus")) : null,
                        Message = !reader.IsDBNull(reader.GetOrdinal("Message")) ? 
                                 reader.GetString(reader.GetOrdinal("Message")) : null,
                        ExtraServices = !reader.IsDBNull(reader.GetOrdinal("ExtraServices")) ? 
                                      reader.GetString(reader.GetOrdinal("ExtraServices")) : null,
                        CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt")),
                        UpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                                   reader.GetString(reader.GetOrdinal("UpdatedAt")) : null
                    };
                    
                    return Ok(new { 
                        success = true, 
                        message = "Foglalás státusza sikeresen frissítve",
                        reservation = reservationData
                    });
                }
            }
            
            return Ok(new { 
                success = true, 
                message = "Foglalás státusza sikeresen frissítve"
            });
        }
        else
        {
            return NotFound(new { 
                success = false, 
                message = "Foglalás nem található vagy nincs változás"
            });
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a foglalás státuszának frissítésekor");
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba a foglalás státuszának frissítése során",
            error = ex.Message,
            stackTrace = ex.StackTrace
        });
    }
}

[HttpPost("CreateReservation")]
public async Task<IActionResult> CreateReservation([FromBody] CreateReservationModel model)
{
    try
    {
        _logger.LogInformation("Új asztalfoglalás létrehozása kérés: {@Model}", model);

        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        // Felhasználónév lekérése
        var userName = await GetUserNameFromSessionAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Érvénytelen session" });
        }

        // Validáció
        if (string.IsNullOrEmpty(model.TableName))
        {
            return BadRequest(new { success = false, message = "Hiányzó asztal név" });
        }

        if (string.IsNullOrEmpty(model.Date) || string.IsNullOrEmpty(model.Time))
        {
            return BadRequest(new { success = false, message = "Hiányzó dátum vagy idő" });
        }

        if (model.Guests < 1)
        {
            return BadRequest(new { success = false, message = "Érvénytelen vendégszám" });
        }

        // ⛔ ELLENŐRZÉS: Zárt nap-e?
        bool isDayClosed = await IsDayClosedAsync(model.Date);
        if (isDayClosed)
        {
            string closedReason = await GetClosedDayReasonAsync(model.Date) ?? "Zárt körű rendezvény";
            
            _logger.LogWarning($"⛔ Foglalás letiltva: Dátum zárt - {model.Date}, Indok: {closedReason}");
            
            return BadRequest(new
            {
                success = false,
                message = $"Ez a nap zárt körű rendezvény miatt nem elérhető: {closedReason}",
                date = model.Date,
                isDayClosed = true,
                closedReason = closedReason,
                suggestion = "Kérjük, válassz másik dátumot."
            });
        }

        // CONVERT FRONTEND TIME TO DATABASE FORMAT
        string dbTime = ConvertToDbTimeFormat(model.Time);
        _logger.LogInformation($"Időpont konvertálva a mentéshez: '{model.Time}' -> '{dbTime}'");

        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Generáljunk egyedi ID-t
        string reservationId = "RES" + DateTime.Now.ToString("yyyyMMddHHmmss") + 
                              new Random().Next(1000, 9999).ToString();

        // Ellenőrizzük, hogy van-e már aktív/approved foglalás ugyanarra az időpontra ÉS ASZTALRA
        var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = @"
            SELECT COUNT(*) as Count 
            FROM Reservations 
            WHERE Status IN ('active', 'approved', 'confirmed')
            AND Date = @Date 
            AND Time = @Time
            AND TableNumber = @TableNumber";
        
        checkCommand.Parameters.AddWithValue("@Date", model.Date);
        checkCommand.Parameters.AddWithValue("@Time", dbTime);
        checkCommand.Parameters.AddWithValue("@TableNumber", model.TableNumber ?? "");
        
        var existingCount = (long)await checkCommand.ExecuteScalarAsync();
        
        if (existingCount > 0)
        {
            _logger.LogWarning($"Foglalás már létezik: {model.Date} {dbTime} asztal #{model.TableNumber}");
            return BadRequest(new { 
                success = false, 
                message = $"Ez az időpont már foglalt erre az asztalra (asztal #{model.TableNumber})" 
            });
        }

        // Foglalás létrehozása - JAVÍTOTT: ExtraServices hozzáadása az SQL-hez
        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Reservations 
            (ReservationId, UserId, TableName, TableNumber, TableLocation, 
             Date, Time, Guests, Status, Message, ExtraServices, CreatedAt, UpdatedAt)
            VALUES 
            (@ReservationId, @UserId, @TableName, @TableNumber, @TableLocation, 
             @Date, @Time, @Guests, @Status, @Message, @ExtraServices, datetime('now'), datetime('now'))";

        command.Parameters.AddWithValue("@ReservationId", reservationId);
        command.Parameters.AddWithValue("@UserId", userName);
        command.Parameters.AddWithValue("@TableName", model.TableName);
        command.Parameters.AddWithValue("@TableNumber", model.TableNumber ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@TableLocation", model.TableLocation ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@Date", model.Date);
        command.Parameters.AddWithValue("@Time", dbTime);
        command.Parameters.AddWithValue("@Guests", model.Guests);
        command.Parameters.AddWithValue("@Status", "active");
        command.Parameters.AddWithValue("@Message", model.Message ?? string.Empty);
        // FONTOS: ExtraServices paraméter hozzáadása
        command.Parameters.AddWithValue("@ExtraServices", 
            !string.IsNullOrEmpty(model.ExtraServices) ? model.ExtraServices : (object)DBNull.Value);

        await command.ExecuteNonQueryAsync();

        _logger.LogInformation(
            "✅ Új asztalfoglalás létrehozva: {ReservationId} - {UserName} - {TableName} (asztal #{TableNumber}) {Date} {Time} (DB: {DbTime}) " +
            "ExtraServices: {ExtraServices}",
            reservationId, userName, model.TableName, model.TableNumber, model.Date, model.Time, dbTime,
            !string.IsNullOrEmpty(model.ExtraServices) ? model.ExtraServices : "Nincs");

        return Ok(new { 
            success = true, 
            message = "Asztalfoglalás sikeresen létrehozva",
            reservationId = reservationId,
            isDayClosed = false,
            reservation = new {
                ReservationId = reservationId,
                UserId = userName,
                TableName = model.TableName,
                TableNumber = model.TableNumber,
                TableLocation = model.TableLocation,
                Date = model.Date,
                Time = model.Time,
                DbTime = dbTime,
                FrontendTime = model.Time,
                Guests = model.Guests,
                Status = "active",
                Message = model.Message,
                ExtraServices = model.ExtraServices, // <-- VISSZAADJUK AZ EXTRA SERVICES-T
                CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            }
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba az asztalfoglalás létrehozásakor");
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba az asztalfoglalás létrehozása során",
            error = ex.Message
        });
    }
}

    [HttpDelete("DeleteReservation/{reservationId}")]
    public async Task<IActionResult> DeleteReservation(string reservationId)
    {
        try
        {
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            // Felhasználónév lekérése
            var userName = await GetUserNameFromSessionAsync(sessionId);
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized(new { success = false, message = "Érvénytelen session" });
            }

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // Ellenőrizzük, hogy a felhasználóé-e a foglalás
            var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "SELECT UserId, UpdatedAt FROM Reservations WHERE ReservationId = @ReservationId";
            checkCommand.Parameters.AddWithValue("@ReservationId", reservationId);

            string? reservationUserId = null;
            string? currentUpdatedAt = null;
            
            using (var reader = await checkCommand.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    reservationUserId = reader.GetString(reader.GetOrdinal("UserId"));
                    currentUpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                                      reader.GetString(reader.GetOrdinal("UpdatedAt")) : null;
                }
            }
            
            if (reservationUserId == null)
            {
                return NotFound(new { success = false, message = "Foglalás nem található" });
            }
            
            if (reservationUserId != userName)
            {
                // Admin ellenőrzése
                var isAdmin = await IsUserAdminAsync(sessionId);
                if (!isAdmin)
                {
                    return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
                }
            }

            // Foglalás törlése
            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Reservations WHERE ReservationId = @ReservationId";
            command.Parameters.AddWithValue("@ReservationId", reservationId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Foglalás törölve: {ReservationId}, utolsó frissítés: {UpdatedAt}", 
                    reservationId, currentUpdatedAt ?? "null");
                return Ok(new { success = true, message = "Foglalás sikeresen törölve" });
            }
            else
            {
                return NotFound(new { success = false, message = "Foglalás nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a foglalás törlésekor");
            return StatusCode(500, new { success = false, message = "Hiba a foglalás törlése során" });
        }
    }

[HttpGet("GetActiveReservation")]
public async Task<IActionResult> GetActiveReservation()
{
    try
    {
        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        // Felhasználónév lekérése
        var userName = await GetUserNameFromSessionAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Érvénytelen session" });
        }

        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT * FROM Reservations 
            WHERE UserId = @UserId 
            AND Status = 'active'
            ORDER BY Date ASC, Time ASC 
            LIMIT 1";

        command.Parameters.AddWithValue("@UserId", userName);

        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                var dbTime = !reader.IsDBNull(reader.GetOrdinal("Time")) ? 
                           reader.GetString(reader.GetOrdinal("Time")) : "";
                
                var reservation = new
                {
                    ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                    UserId = reader.GetString(reader.GetOrdinal("UserId")),
                    TableName = reader.GetString(reader.GetOrdinal("TableName")),
                    TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                 reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                    TableLocation = !reader.IsDBNull(reader.GetOrdinal("TableLocation")) ? 
                                  reader.GetString(reader.GetOrdinal("TableLocation")) : null,
                    Date = reader.GetString(reader.GetOrdinal("Date")),
                    Time = dbTime,
                    FrontendTime = ConvertToFrontendTimeFormat(dbTime),
                    Guests = reader.GetInt32(reader.GetOrdinal("Guests")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    OrderId = !reader.IsDBNull(reader.GetOrdinal("OrderId")) ? 
                             reader.GetString(reader.GetOrdinal("OrderId")) : null,
                    Message = !reader.IsDBNull(reader.GetOrdinal("Message")) ? 
                             reader.GetString(reader.GetOrdinal("Message")) : null,
                    ExtraServices = !reader.IsDBNull(reader.GetOrdinal("ExtraServices")) ?  // <-- ÚJ
                                   reader.GetString(reader.GetOrdinal("ExtraServices")) : null,
                    CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt")),
                    UpdatedAt = !reader.IsDBNull(reader.GetOrdinal("UpdatedAt")) ? 
                               reader.GetString(reader.GetOrdinal("UpdatedAt")) : null
                };

                return Ok(new { 
                    success = true, 
                    hasActiveReservation = true,
                    reservation = reservation
                });
            }
        }

        return Ok(new { 
            success = true, 
            hasActiveReservation = false,
            message = "Nincs aktív asztalfoglalás"
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba az aktív foglalás lekérdezésekor");
        return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
    }
}

    // ========== HELPER METHODS ==========

    private async Task<bool> IsDayClosedAsync(string date)
    {
        try
        {
            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) as Count 
                FROM ClosedDays 
                WHERE Date = @Date 
                AND IsActive = 1";

            command.Parameters.AddWithValue("@Date", date);

            var count = Convert.ToInt64(await command.ExecuteScalarAsync());
            return count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string?> GetClosedDayReasonAsync(string date)
    {
        try
        {
            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Reason 
                FROM ClosedDays 
                WHERE Date = @Date 
                AND IsActive = 1 
                LIMIT 1";

            command.Parameters.AddWithValue("@Date", date);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch
        {
            return "Zárt körű rendezvény";
        }
    }

    private async Task NotifyFutureReservationsAsync(MySqlConnection connection, string date, string reason)
    {
        try
        {
            // Keressük a jövőbeli foglalásokat erre a napra
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT ReservationId, UserId, TableName, Time, Guests
                FROM Reservations 
                WHERE Date > @TodayDate
                AND Status IN ('active', 'approved', 'confirmed')
                LIMIT 50"; // Limit azért, hogy ne küldjünk túl sok értesítést

            command.Parameters.AddWithValue("@TodayDate", DateTime.Today.ToString("yyyy-MM-dd"));

            var futureReservations = new List<FutureReservationNotification>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    futureReservations.Add(new FutureReservationNotification
                    {
                        ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                        UserId = reader.GetString(reader.GetOrdinal("UserId")),
                        TableName = reader.GetString(reader.GetOrdinal("TableName")),
                        Time = reader.GetString(reader.GetOrdinal("Time")),
                        Guests = reader.GetInt32(reader.GetOrdinal("Guests"))
                    });
                }
            }

            if (futureReservations.Count > 0)
            {
                _logger.LogInformation($"📢 {futureReservations.Count} jövőbeli foglalás értesítésre kerül a zárt nap miatt: {date}");
                
                // Itt implementálhatod az értesítés küldését:
                // - Email küldés
                // - Push notification
                // - Naplózás
                // - Stb.
                
                // Példa: naplózzuk az értesítéseket
                foreach (var reservation in futureReservations)
                {
                    _logger.LogInformation($"   📝 Értesítés: {reservation.ReservationId} - {reservation.UserId} - {reservation.TableName} {reservation.Time}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a jövőbeli foglalások értesítésekor");
        }
    }

private async Task<string?> GetUserNameFromSessionAsync(string sessionId)
{
    try
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserName FROM Session WHERE SessionID = @SessionId";
        command.Parameters.AddWithValue("@SessionId", sessionId);

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }
    catch
    {
        return null;
    }
}

private async Task<bool> IsUserAdminAsync(string sessionId)
{
    try
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT u.UserName
            FROM Session s
            JOIN User u ON s.UserName = u.UserName
            WHERE s.SessionID = @SessionId AND u.IsAdmin = 1";

        command.Parameters.AddWithValue("@SessionId", sessionId);

        var result = await command.ExecuteScalarAsync();
        return result != null;
    }
    catch
    {
        return false;
    }
}

    // ========== TIME FORMAT CONVERSION METHODS ==========

    private string ConvertToFrontendTimeFormat(string dbTime)
    {
        if (string.IsNullOrEmpty(dbTime))
            return dbTime;

        // If already in frontend format (contains "-"), return as is
        if (dbTime.Contains("-"))
            return dbTime;

        // Convert from "HH:MM" to "HH:MM-HH:MM" (2 hour slots)
        if (dbTime.Length == 5 && dbTime.Contains(":"))
        {
            var timeParts = dbTime.Split(':');
            if (timeParts.Length == 2 && int.TryParse(timeParts[0], out int hour))
            {
                // Standard 2-hour time slots used in frontend
                switch (dbTime)
                {
                    case "10:00": return "10:00-12:00";
                    case "12:00": return "12:00-14:00";
                    case "14:00": return "14:00-16:00";
                    case "16:00": return "16:00-18:00";
                    case "18:00": return "18:00-20:00";
                    default:
                        int endHour = hour + 2;
                        if (endHour > 23) endHour = 23;
                        return $"{dbTime}-{endHour:00}:00";
                }
            }
        }

        return dbTime;
    }

    private string ConvertToDbTimeFormat(string frontendTime)
    {
        if (string.IsNullOrEmpty(frontendTime))
            return frontendTime;

        // If already in DB format (doesn't contain "-"), return as is
        if (!frontendTime.Contains("-"))
            return frontendTime;

        // Extract start time from "HH:MM-HH:MM"
        var startTime = frontendTime.Split('-')[0].Trim();
        
        // Ensure it's in "HH:MM" format
        if (startTime.Length == 5 && startTime.Contains(":"))
        {
            return startTime;
        }

        return frontendTime;
    }

    // ========== DEBUG ENDPOINTS ==========

[HttpGet("DebugReservations")]
public async Task<IActionResult> DebugReservations()
{
    try
    {
        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT 
                Id,
                ReservationId,
                UserId,
                TableName,
                TableNumber,
                Date,
                Time,
                Guests,
                Status,
                Message,
                ExtraServices,  -- <-- ÚJ: extra szolgáltatások
                CreatedAt,
                UpdatedAt
            FROM Reservations 
            ORDER BY Date DESC, Time DESC";
        
        var reservations = new List<Dictionary<string, object>>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var reservation = new Dictionary<string, object>();
                
                // Read each column explicitly
                reservation["Id"] = reader.GetInt32(0);
                reservation["ReservationId"] = reader.GetString(1);
                reservation["UserId"] = reader.GetString(2);
                reservation["TableName"] = reader.GetString(3);
                reservation["TableNumber"] = !reader.IsDBNull(4) ? reader.GetString(4) : "NULL";
                reservation["Date"] = reader.GetString(5);
                reservation["Time"] = !reader.IsDBNull(6) ? reader.GetString(6) : "NULL";
                reservation["FrontendTime"] = ConvertToFrontendTimeFormat(!reader.IsDBNull(6) ? reader.GetString(6) : "");
                reservation["Guests"] = reader.GetInt32(7);
                reservation["Status"] = reader.GetString(8);
                reservation["Message"] = !reader.IsDBNull(9) ? reader.GetString(9) : "NULL";
                reservation["ExtraServices"] = !reader.IsDBNull(10) ? reader.GetString(10) : "NULL";  // <-- ÚJ
                reservation["CreatedAt"] = reader.GetString(11);
                reservation["UpdatedAt"] = !reader.IsDBNull(12) ? reader.GetString(12) : "NULL";
                
                reservations.Add(reservation);
            }
        }

        return Ok(new 
        { 
            success = true, 
            count = reservations.Count,
            reservations = reservations,
            message = $"Összesen {reservations.Count} foglalás található az adatbázisban"
        });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba a debuggolás során",
            error = ex.Message,
            stackTrace = ex.StackTrace
        });
    }
}

    [HttpGet("TestTimeConversion")]
    public IActionResult TestTimeConversion()
    {
        var testTimes = new[]
        {
            "10:00", "12:00", "14:00", "16:00", "18:00",
            "10:00-12:00", "12:00-14:00", "14:00-16:00", "16:00-18:00", "18:00-20:00",
            "09:00", "13:00", "19:00"
        };

        var results = new List<object>();
        foreach (var time in testTimes)
        {
            var dbFormat = ConvertToDbTimeFormat(time);
            var frontendFormat = ConvertToFrontendTimeFormat(dbFormat);
            
            results.Add(new
            {
                Original = time,
                ToDb = dbFormat,
                ToFrontend = frontendFormat,
                IsValid = frontendFormat.Contains("-") && dbFormat.Length == 5 && dbFormat.Contains(":")
            });
        }

        return Ok(new { success = true, conversions = results });
    }

    [HttpGet("GetSchemaInfo")]
    public async Task<IActionResult> GetSchemaInfo()
    {
        try
        {
            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1. Táblák listázása
            var tablesCommand = connection.CreateCommand();
            tablesCommand.CommandText = @"
                SELECT name, sql 
                FROM sqlite_master 
                WHERE type='table' 
                ORDER BY name";
            
            var tables = new List<object>();
            await using (var reader = await tablesCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    tables.Add(new
                    {
                        TableName = reader.GetString(0),
                        Definition = reader.GetString(1)
                    });
                }
            }

            // 2. Reservations tábla oszlopai
            var columnsCommand = connection.CreateCommand();
            columnsCommand.CommandText = @"
                PRAGMA table_info(Reservations)";
            
            var columns = new List<object>();
            await using (var reader = await columnsCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns.Add(new
                    {
                        Cid = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Type = reader.GetString(2),
                        NotNull = reader.GetInt32(3) == 1,
                        DefaultValue = !reader.IsDBNull(4) ? reader.GetString(4) : null,
                        Pk = reader.GetInt32(5) == 1
                    });
                }
            }

            // 3. ClosedDays tábla oszlopai
            var closedDaysColumnsCommand = connection.CreateCommand();
            closedDaysColumnsCommand.CommandText = @"
                PRAGMA table_info(ClosedDays)";
            
            var closedDaysColumns = new List<object>();
            await using (var reader = await closedDaysColumnsCommand.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    closedDaysColumns.Add(new
                    {
                        Cid = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Type = reader.GetString(2),
                        NotNull = reader.GetInt32(3) == 1,
                        DefaultValue = !reader.IsDBNull(4) ? reader.GetString(4) : null,
                        Pk = reader.GetInt32(5) == 1
                    });
                }
            }

            return Ok(new
            {
                success = true,
                tables = tables,
                reservationsColumns = columns,
                closedDaysColumns = closedDaysColumns,
                message = "Adatbázis séma információk"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                success = false,
                message = "Hiba a séma információk lekérdezésekor",
                error = ex.Message,
                stackTrace = ex.StackTrace
            });
        }
    }
    [HttpPost("SendTomorrowReservationReminders")]
public async Task<IActionResult> SendTomorrowReservationReminders()
{
    try
    {
        _logger.LogInformation("📧 Holnapi foglalási emlékeztetők küldésének indítása...");

        // Ellenőrizzük, hogy admin-e
        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs érvényes session" });
        }

        var isAdmin = await IsUserAdminAsync(sessionId);
        if (!isAdmin)
        {
            return StatusCode(403, new { success = false, message = "Csak admin felhasználók küldhetnek emlékeztetőket" });
        }

        // Holnapi dátum
        var tomorrow = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
        _logger.LogInformation($"Holnapi dátum: {tomorrow}");

        using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        // Lekérjük a holnapi aktív foglalásokat a felhasználói adatokkal
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.ReservationId, 
                   r.UserId, 
                   r.TableName, 
                   r.TableNumber, 
                   r.TableLocation, 
                   r.Date, 
                   r.Time, 
                   r.Guests, 
                   r.Status,
                   u.Email as UserEmail
            FROM Reservations r
            LEFT JOIN User u ON r.UserId = u.UserName
            WHERE r.Date = @TomorrowDate
            AND r.Status IN ('active', 'approved', 'confirmed')
            ORDER BY r.Time ASC";

        command.Parameters.AddWithValue("@TomorrowDate", tomorrow);

        var reservations = new List<ReservationReminderData>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var userEmail = !reader.IsDBNull(reader.GetOrdinal("UserEmail")) ? 
                               reader.GetString(reader.GetOrdinal("UserEmail")) : null;
                
                // Csak azokat vesszük, akiknek van email címe
                if (!string.IsNullOrEmpty(userEmail))
                {
                    reservations.Add(new ReservationReminderData
                    {
                        ReservationId = reader.GetString(reader.GetOrdinal("ReservationId")),
                        UserName = reader.GetString(reader.GetOrdinal("UserId")),
                        UserEmail = userEmail,
                        TableName = reader.GetString(reader.GetOrdinal("TableName")),
                        TableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                     reader.GetString(reader.GetOrdinal("TableNumber")) : null,
                        TableLocation = !reader.IsDBNull(reader.GetOrdinal("TableLocation")) ? 
                                       reader.GetString(reader.GetOrdinal("TableLocation")) : null,
                        Date = reader.GetString(reader.GetOrdinal("Date")),
                        Time = ConvertToFrontendTimeFormat(reader.GetString(reader.GetOrdinal("Time"))),
                        Guests = reader.GetInt32(reader.GetOrdinal("Guests")),
                        Status = reader.GetString(reader.GetOrdinal("Status"))
                    });
                }
                else
                {
                    _logger.LogWarning($"⚠️ Foglalás {reader.GetString(reader.GetOrdinal("ReservationId"))} - nincs email cím a felhasználóhoz, kihagyva");
                }
            }
        }

        _logger.LogInformation($"📋 Holnapi foglalások email címmel: {reservations.Count} db");

        if (reservations.Count == 0)
        {
            return Ok(new
            {
                success = true,
                message = "Nincs holnapi foglalás, amire emlékeztetőt kellene küldeni",
                tomorrow = tomorrow,
                sentCount = 0,
                reservations = new List<object>()
            });
        }

        // Email küldés minden foglaláshoz
        var successCount = 0;
        var failedReservations = new List<string>();

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        foreach (var reservation in reservations)
        {
            try
            {
                var emailModel = new
                {
                    UserName = reservation.UserName,
                    Email = reservation.UserEmail,
                    ReservationId = reservation.ReservationId,
                    TableName = reservation.TableName,
                    TableNumber = reservation.TableNumber,
                    Date = ConvertToReadableDate(reservation.Date),
                    Time = reservation.Time,
                    Guests = reservation.Guests,
                    TableLocation = reservation.TableLocation ?? "Éttermünkben"
                };

                var jsonContent = System.Text.Json.JsonSerializer.Serialize(emailModel);
                var content = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                // Itt feltételezzük, hogy az EmailController a /Email endpointon van
                var response = await httpClient.PostAsync("http://localhost:5000/Email/SendReservationReminder", content);
                
                if (response.IsSuccessStatusCode)
                {
                    successCount++;
                    _logger.LogInformation($"✅ Emlékeztető elküldve: {reservation.ReservationId} -> {reservation.UserEmail}");
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    failedReservations.Add($"{reservation.ReservationId} (HTTP {response.StatusCode}): {errorContent}");
                    _logger.LogError($"❌ Sikertelen email küldés: {reservation.ReservationId}, HTTP {response.StatusCode}");
                }
            }
            catch (Exception emailEx)
            {
                failedReservations.Add($"{reservation.ReservationId}: {emailEx.Message}");
                _logger.LogError(emailEx, $"❌ Hiba az emlékeztető küldésekor: {reservation.ReservationId}");
            }
        }

        _logger.LogInformation($"📧 Emlékeztetők küldése befejezve: Sikeres: {successCount}, Sikertelen: {failedReservations.Count}");

        return Ok(new
        {
            success = true,
            message = $"Emlékeztetők küldése befejezve. Sikeres: {successCount}, Sikertelen: {failedReservations.Count}",
            tomorrow = tomorrow,
            sentCount = successCount,
            failedCount = failedReservations.Count,
            failedReservations = failedReservations,
            reservations = reservations.Select(r => new
            {
                r.ReservationId,
                r.UserName,
                r.UserEmail,
                r.TableName,
                r.TableNumber,
                r.Date,
                r.Time,
                r.Guests
            })
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Hiba a holnapi foglalási emlékeztetők küldésekor");
        return StatusCode(500, new
        {
            success = false,
            message = "Hiba a foglalási emlékeztetők küldése során",
            error = ex.Message,
            stackTrace = ex.StackTrace
        });
    }
}

// Segédmetódus a dátum formázásához
private string ConvertToReadableDate(string date)
{
    if (string.IsNullOrEmpty(date))
        return date;

    try
    {
        var parsedDate = DateTime.Parse(date);
        return parsedDate.ToString("yyyy. MM. dd.");
    }
    catch
    {
        return date;
    }
}


// ========== MODELLEK ==========

public class ReservationActionModel
{
    public string ReservationId { get; set; } = string.Empty;
}

public class UpdateReservationStatusModel
{
    public string ReservationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? OrderId { get; set; }

    public string? ExtraServices { get; set; }
    public ReservationDataModel? ReservationData { get; set; }
}

public class CreateReservationModel
{
    public string TableName { get; set; } = string.Empty;
    public string? TableNumber { get; set; }
    public string? TableLocation { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public int Guests { get; set; }
    public string? Message { get; set; }
    public string? ExtraServices { get; set; } // <-- ÚJ: Extra szolgáltatások JSON stringként
}

public class TimeSlotCheckModel
{
    public string Date { get; set; } = string.Empty;
    public string TimeSlot { get; set; } = string.Empty;
    public string? TableNumber { get; set; }
}

public class CloseDayModel
{
    public string Date { get; set; } = string.Empty;
    public string? Reason { get; set; }
}

private class FutureReservationNotification
{
    public string ReservationId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public int Guests { get; set; }
}
public class ReservationDataModel
{
    public string? TableName { get; set; }
    public string? TableNumber { get; set; }
    public string? TableLocation { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public int? Guests { get; set; }
    public string? Message { get; set; }
    public string? ExtraServices { get; set; }
}
private class ReservationReminderData
{
    public string ReservationId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserEmail { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? TableNumber { get; set; }
    public string? TableLocation { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public int Guests { get; set; }
    public string Status { get; set; } = string.Empty;
}
}
