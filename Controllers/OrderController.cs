// OrderController.cs - RÉSZLETES DEBUG VERZIÓ
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Dynamic;
using MySql.Data.MySqlClient;
using System.Diagnostics;
using System.Text;

[Route("[controller]")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly string _connectionString;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IConfiguration configuration, ILogger<OrderController> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? throw new InvalidOperationException("Connection string not found");
        _logger = logger;
        
        // DEBUG: Induláskor logoljuk a connection string-et (csak az elejét)
        var maskedConn = _connectionString.Length > 50 ? _connectionString.Substring(0, 50) + "..." : _connectionString;
        _logger.LogInformation("🔧 OrderController inicializálva, ConnectionString: {ConnectionString}", maskedConn);
    }

    // ==================== HELPER METÓDUSOK ====================

    private async Task<string?> GetUserNameFromSessionAsync(string sessionId)
    {
        try
        {
            _logger.LogDebug("🔍 GetUserNameFromSessionAsync: {SessionId}", sessionId);
            
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT UserName FROM Session WHERE SessionID = @SessionId";
            command.Parameters.AddWithValue("@SessionId", sessionId);

            var result = await command.ExecuteScalarAsync();
            var userName = result?.ToString();
            
            _logger.LogDebug("✅ Sessionhez tartozó felhasználó: {UserName}", userName ?? "NULL");
            return userName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba a session lekérdezésekor, SessionId: {SessionId}", sessionId);
            return null;
        }
    }

    private async Task<bool> IsUserAdminAsync(string sessionId)
    {
        try
        {
            _logger.LogDebug("🔍 IsUserAdminAsync: {SessionId}", sessionId);
            
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT u.UserName
                FROM Session s
                JOIN User u ON s.UserName = u.UserName
                WHERE s.SessionID = @SessionId AND u.IsAdmin = 1";

            command.Parameters.AddWithValue("@SessionId", sessionId);

            var result = await command.ExecuteScalarAsync();
            var isAdmin = result != null;
            
            _logger.LogDebug("✅ Admin jogosultság: {IsAdmin}", isAdmin);
            return isAdmin;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba az admin ellenőrzésekor, SessionId: {SessionId}", sessionId);
            return false;
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
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ SafeGetString hiba: {ColumnName} - {Message}", columnName, ex.Message);
            return null;
        }
    }

    private long SafeGetLong(MySqlDataReader reader, string columnName)
    {
        try
        {
            var idx = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(idx))
                return 0;
            
            return Convert.ToInt64(reader.GetValue(idx));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ SafeGetLong hiba: {ColumnName} - {Message}", columnName, ex.Message);
            return 0;
        }
    }

    private int SafeGetInt(MySqlDataReader reader, string columnName)
    {
        try
        {
            var idx = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(idx))
                return 0;
            
            return Convert.ToInt32(reader.GetValue(idx));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("⚠️ SafeGetInt hiba: {ColumnName} - {Message}", columnName, ex.Message);
            return 0;
        }
    }

    // ==================== API METÓDUSOK ====================

    [HttpGet("GetUserOrders")]
    public async Task<IActionResult> GetUserOrders()
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("🚀 [START] GetUserOrders hívás");
        
        try
        {
            // 1. Session ellenőrzés
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            _logger.LogDebug("📝 Session cookie: {SessionId}", sessionId ?? "NINCS");
            
            if (string.IsNullOrEmpty(sessionId))
            {
                _logger.LogWarning("⚠️ Nincs session cookie");
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            // 2. Felhasználónév lekérése
            var userName = await GetUserNameFromSessionAsync(sessionId);
            _logger.LogDebug("📝 Felhasználónév: {UserName}", userName ?? "NULL");
            
            if (string.IsNullOrEmpty(userName))
            {
                _logger.LogWarning("⚠️ Érvénytelen session - nincs felhasználó");
                return Unauthorized(new { success = false, message = "Érvénytelen session" });
            }

            // 3. Adatbázis kapcsolat
            _logger.LogDebug("🔗 Adatbázis kapcsolat létrehozása...");
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogDebug("✅ Adatbázis kapcsolat nyitva");

            // 4. SQL lekérdezés
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*,
                       (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount,
                       r.TableName as ReservationTableName,
                       r.TableNumber as ReservationTableNumber,
                       r.Date as ReservationDate,
                       r.Time as ReservationTime,
                       r.Guests as ReservationGuests
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                WHERE o.UserId = @UserId
                ORDER BY o.OrderDate DESC, o.CreatedAt DESC";

            command.Parameters.AddWithValue("@UserId", userName);
            
            _logger.LogDebug("📊 SQL lekérdezés: {Sql}", command.CommandText);
            _logger.LogDebug("📊 Paraméter: UserId = {UserId}", userName);

            // 5. Adatok olvasása
            var orders = new List<dynamic>();
            int rowCount = 0;
            
            await using (var reader = await command.ExecuteReaderAsync())
            {
                _logger.LogDebug("📖 Adatok olvasása...");
                
                while (await reader.ReadAsync())
                {
                    rowCount++;
                    _logger.LogDebug("📄 Sor #{RowCount} feldolgozása", rowCount);
                    
                    try
                    {
                        dynamic order = new ExpandoObject();
                        
                        order.OrderId = reader.GetString(reader.GetOrdinal("OrderId"));
                        order.UserId = reader.GetString(reader.GetOrdinal("UserId"));
                        order.UserName = reader.GetString(reader.GetOrdinal("UserName"));
                        order.OrderDate = SafeGetString(reader, "OrderDate");
                        order.TotalPrice = SafeGetLong(reader, "TotalPrice");
                        order.Status = reader.GetString(reader.GetOrdinal("Status"));
                        order.ItemsCount = SafeGetInt(reader, "ItemsCount");
                        order.ServiceFee = SafeGetLong(reader, "ServiceFee");
                        order.ReservationId = SafeGetString(reader, "ReservationId");
                        order.Notes = SafeGetString(reader, "Notes");
                        order.CreatedAt = SafeGetString(reader, "CreatedAt");
                        order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                        
                        _logger.LogDebug("   📦 Rendelés: {OrderId}, Összeg: {TotalPrice} Ft, Státusz: {Status}", 
                            order.OrderId, order.TotalPrice, order.Status);
                        
                        if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                        {
                            order.ReservationDetails = new
                            {
                                TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                                TableNumber = SafeGetString(reader, "ReservationTableNumber"),
                                Date = SafeGetString(reader, "ReservationDate"),
                                Time = SafeGetString(reader, "ReservationTime"),
                                Guests = SafeGetInt(reader, "ReservationGuests")
                            };
                            _logger.LogDebug("   🪑 Asztalfoglalás: {TableName}, {Date} {Time}", 
                                order.ReservationDetails.TableName, 
                                order.ReservationDetails.Date, 
                                order.ReservationDetails.Time);
                        }

                        orders.Add(order);
                    }
                    catch (Exception rowEx)
                    {
                        _logger.LogError(rowEx, "❌ Hiba a sor #{RowCount} feldolgozásakor", rowCount);
                        // Nem szakítjuk meg a feldolgozást, csak logoljuk
                    }
                }
            }
            
            _logger.LogInformation("📊 Összesen {RowCount} rendelés betöltve", rowCount);

            // 6. Tételek betöltése
            _logger.LogDebug("🔍 Tételek betöltése...");
            int totalItems = 0;
            
            foreach (dynamic order in orders)
            {
                var items = await GetOrderItemsAsync(order.OrderId);
                order.Items = items;
                totalItems += ((IEnumerable<dynamic>)items).Count();
                _logger.LogDebug("   📋 {OrderId}: {ItemCount} tétel", order.OrderId, ((IEnumerable<dynamic>)items).Count());
            }
            
            _logger.LogInformation("📊 Összesen {TotalItems} tétel betöltve", totalItems);

            stopwatch.Stop();
            _logger.LogInformation("✅ [END] GetUserOrders sikeresen befejezve, {OrderCount} rendelés, Idő: {ElapsedMs} ms", 
                orders.Count, stopwatch.ElapsedMilliseconds);

            return Ok(new { success = true, orders = orders, debug = new { elapsedMs = stopwatch.ElapsedMilliseconds, rowCount, totalItems } });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "💥 KRITIKUS HIBA a GetUserOrders-ben, Idő: {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            
            // Részletes hiba információ
            var errorDetails = new
            {
                message = ex.Message,
                stackTrace = ex.StackTrace,
                innerMessage = ex.InnerException?.Message,
                innerStackTrace = ex.InnerException?.StackTrace,
                elapsedMs = stopwatch.ElapsedMilliseconds
            };
            
            return StatusCode(500, new { 
                success = false, 
                message = "Hiba az adatok lekérdezésekor", 
                error = errorDetails 
            });
        }
    }

    [HttpGet("GetAllOrders")]
    public async Task<IActionResult> GetAllOrders()
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("🚀 [START] GetAllOrders hívás");
        
        try
        {
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            _logger.LogDebug("📝 Session cookie: {SessionId}", sessionId ?? "NINCS");
            
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            var isAdmin = await IsUserAdminAsync(sessionId);
            _logger.LogDebug("📝 Admin jogosultság: {IsAdmin}", isAdmin);
            
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogDebug("✅ Adatbázis kapcsolat nyitva");

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*,
                       (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount
                FROM Orders o
                ORDER BY o.OrderDate DESC, o.CreatedAt DESC";

            var orders = new List<dynamic>();
            int rowCount = 0;
            
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    rowCount++;
                    dynamic order = new ExpandoObject();
                    
                    order.OrderId = reader.GetString(reader.GetOrdinal("OrderId"));
                    order.UserId = reader.GetString(reader.GetOrdinal("UserId"));
                    order.UserName = reader.GetString(reader.GetOrdinal("UserName"));
                    order.OrderDate = SafeGetString(reader, "OrderDate");
                    order.TotalPrice = SafeGetLong(reader, "TotalPrice");
                    order.Status = reader.GetString(reader.GetOrdinal("Status"));
                    order.ItemsCount = SafeGetInt(reader, "ItemsCount");
                    order.ServiceFee = SafeGetLong(reader, "ServiceFee");
                    order.ReservationId = SafeGetString(reader, "ReservationId");
                    order.CreatedAt = SafeGetString(reader, "CreatedAt");
                    order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                    
                    orders.Add(order);
                }
            }
            
            stopwatch.Stop();
            _logger.LogInformation("✅ [END] GetAllOrders sikeresen befejezve, {OrderCount} rendelés, Idő: {ElapsedMs} ms", 
                orders.Count, stopwatch.ElapsedMilliseconds);

            return Ok(new { success = true, orders = orders });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "💥 KRITIKUS HIBA a GetAllOrders-ben");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor", error = ex.Message });
        }
    }

    [HttpGet("GetOrder/{orderId}")]
    public async Task<IActionResult> GetOrder(string orderId)
    {
        _logger.LogInformation("🔍 GetOrder hívás: {OrderId}", orderId);
        
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

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*,
                       r.TableName as ReservationTableName,
                       r.TableNumber as ReservationTableNumber,
                       r.Date as ReservationDate,
                       r.Time as ReservationTime,
                       r.Guests as ReservationGuests
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                WHERE o.OrderId = @OrderId";

            command.Parameters.AddWithValue("@OrderId", orderId);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    var orderUserId = reader.GetString(reader.GetOrdinal("UserId"));
                    var isAdmin = await IsUserAdminAsync(sessionId);
                    
                    if (orderUserId != userName && !isAdmin)
                    {
                        _logger.LogWarning("⚠️ Jogosulatlan hozzáférés: User={UserName}, OrderUser={OrderUserId}", userName, orderUserId);
                        return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
                    }

                    dynamic order = new ExpandoObject();
                    
                    order.OrderId = reader.GetString(reader.GetOrdinal("OrderId"));
                    order.UserId = reader.GetString(reader.GetOrdinal("UserId"));
                    order.UserName = reader.GetString(reader.GetOrdinal("UserName"));
                    order.OrderDate = SafeGetString(reader, "OrderDate");
                    order.TotalPrice = SafeGetLong(reader, "TotalPrice");
                    order.Status = reader.GetString(reader.GetOrdinal("Status"));
                    order.ServiceFee = SafeGetLong(reader, "ServiceFee");
                    order.ReservationId = SafeGetString(reader, "ReservationId");
                    order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                    order.Notes = SafeGetString(reader, "Notes");
                    order.CreatedAt = SafeGetString(reader, "CreatedAt");
                    
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        order.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            TableNumber = SafeGetString(reader, "ReservationTableNumber"),
                            Date = SafeGetString(reader, "ReservationDate"),
                            Time = SafeGetString(reader, "ReservationTime"),
                            Guests = SafeGetInt(reader, "ReservationGuests")
                        };
                    }
                    
                    order.Items = await GetOrderItemsAsync(orderId);
                    
                    _logger.LogInformation("✅ GetOrder sikeres: {OrderId}", orderId);
                    return Ok(new { success = true, order = order });
                }
                else
                {
                    _logger.LogWarning("⚠️ Rendelés nem található: {OrderId}", orderId);
                    return NotFound(new { success = false, message = "Rendelés nem található" });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Hiba a rendelés lekérdezésekor: {OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor", error = ex.Message });
        }
    }

    private async Task<List<dynamic>> GetOrderItemsAsync(string orderId)
    {
        var items = new List<dynamic>();
        
        try
        {
            _logger.LogDebug("🔍 GetOrderItemsAsync: {OrderId}", orderId);
            
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM OrderItems 
                WHERE OrderId = @OrderId 
                ORDER BY ItemName";

            command.Parameters.AddWithValue("@OrderId", orderId);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dynamic item = new ExpandoObject();
                    
                    item.ItemName = reader.GetString(reader.GetOrdinal("ItemName"));
                    item.ItemDescription = SafeGetString(reader, "ItemDescription");
                    item.Quantity = reader.GetInt32(reader.GetOrdinal("Quantity"));
                    item.UnitPrice = SafeGetLong(reader, "UnitPrice");
                    item.TotalPrice = SafeGetLong(reader, "TotalPrice");
                    item.ConsumptionType = SafeGetString(reader, "ConsumptionType") ?? "restaurant";
                    item.ReservationDate = SafeGetString(reader, "ReservationDate");
                    item.ReservationTime = SafeGetString(reader, "ReservationTime");
                    
                    items.Add(item);
                    _logger.LogDebug("   📦 Tétel: {ItemName}, {Quantity}x {UnitPrice} Ft", item.ItemName, item.Quantity, item.UnitPrice);
                }
            }
            
            _logger.LogDebug("✅ GetOrderItemsAsync: {ItemCount} tétel betöltve", items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba a tételek lekérdezésekor: {OrderId}", orderId);
        }

        return items;
    }

    // ==================== EGÉSZSÉG ELLENŐRZÉS ====================
    
    [HttpGet("Health")]
    public IActionResult Health()
    {
        _logger.LogInformation("🏥 Health check hívás");
        
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            var dbStatus = "OK";
            _logger.LogInformation("✅ Adatbázis kapcsolat OK");
            connection.Close();
            
            return Ok(new { 
                status = "healthy", 
                timestamp = DateTime.Now,
                database = dbStatus,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 Adatbázis kapcsolat hiba");
            return StatusCode(500, new { 
                status = "unhealthy", 
                timestamp = DateTime.Now,
                error = ex.Message
            });
        }
    }

    // ==================== RENDELÉS LÉTREHOZÁS ====================

    private string GenerateOrderId()
    {
        var id = "ORD" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999).ToString();
        _logger.LogDebug("🔑 Új OrderId generálva: {OrderId}", id);
        return id;
    }

    [HttpPost("CreateOrder")]
    public async Task<IActionResult> CreateOrder([FromBody] OrderModel model)
    {
        _logger.LogInformation("🚀 CreateOrder hívás: {@Model}", model);
        
        try
        {
            if (model == null)
            {
                _logger.LogWarning("⚠️ CreateOrder: model null");
                return BadRequest(new { success = false, message = "Hiányzó rendelési adatok." });
            }

            if (string.IsNullOrWhiteSpace(model.UserId))
            {
                _logger.LogWarning("⚠️ CreateOrder: UserId hiányzik");
                return BadRequest(new { success = false, message = "Hiányzó felhasználói adatok." });
            }

            _logger.LogInformation("📝 Rendelés létrehozása - UserId: {UserId}, Items: {ItemCount}, Total: {TotalAmount} Ft", 
                model.UserId, model.Items?.Count ?? 0, model.TotalAmount);

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();
            _logger.LogDebug("✅ Adatbázis kapcsolat nyitva");

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                string orderId = GenerateOrderId();
                int itemsCount = model.Items?.Count ?? 1;
                long totalPrice = (long)model.TotalAmount;
                long serviceFee = (long)model.ServiceFee;
                
                _logger.LogDebug("📊 Rendelés adatok: OrderId={OrderId}, ItemsCount={ItemsCount}, TotalPrice={TotalPrice}, ServiceFee={ServiceFee}", 
                    orderId, itemsCount, totalPrice, serviceFee);
                
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Orders (OrderId, UserId, UserName, OrderDate, TotalPrice, Status, 
                                        ServiceFee, ItemsCount, ReservationId, Notes, 
                                        PaymentMethod, CreatedAt)
                    VALUES (@OrderId, @UserId, @UserName, NOW(), @TotalPrice, 'pending', 
                            @ServiceFee, @ItemsCount, @ReservationId, @Notes, 
                            @PaymentMethod, NOW())";

                command.Parameters.AddWithValue("@OrderId", orderId);
                command.Parameters.AddWithValue("@UserId", model.UserId);
                command.Parameters.AddWithValue("@UserName", model.UserId);
                command.Parameters.AddWithValue("@TotalPrice", totalPrice);
                command.Parameters.AddWithValue("@ServiceFee", serviceFee);
                command.Parameters.AddWithValue("@ItemsCount", itemsCount);
                command.Parameters.AddWithValue("@ReservationId", string.IsNullOrEmpty(model.ReservationId) ? DBNull.Value : (object)model.ReservationId);
                command.Parameters.AddWithValue("@Notes", model.Notes ?? string.Empty);
                command.Parameters.AddWithValue("@PaymentMethod", model.PaymentMethod ?? "card");

                var result = await command.ExecuteNonQueryAsync();
                _logger.LogDebug("✅ Orders beszúrás eredménye: {Result}", result);

                if (model.Items != null)
                {
                    foreach (var item in model.Items)
                    {
                        command = connection.CreateCommand();
                        command.CommandText = @"
                            INSERT INTO OrderItems (OrderId, ItemName, ItemDescription, Quantity, 
                                                   UnitPrice, TotalPrice, ConsumptionType, 
                                                   ReservationDate, ReservationTime)
                            VALUES (@OrderId, @ItemName, @ItemDescription, @Quantity, 
                                    @UnitPrice, @TotalPrice, @ConsumptionType, 
                                    @ReservationDate, @ReservationTime)";

                        command.Parameters.AddWithValue("@OrderId", orderId);
                        command.Parameters.AddWithValue("@ItemName", item.Name);
                        command.Parameters.AddWithValue("@ItemDescription", item.Description ?? string.Empty);
                        command.Parameters.AddWithValue("@Quantity", item.Quantity);
                        command.Parameters.AddWithValue("@UnitPrice", (long)item.Price);
                        command.Parameters.AddWithValue("@TotalPrice", (long)(item.Price * item.Quantity));
                        command.Parameters.AddWithValue("@ConsumptionType", item.Consumption ?? "restaurant");
                        command.Parameters.AddWithValue("@ReservationDate", string.IsNullOrEmpty(item.Date) ? DBNull.Value : (object)item.Date);
                        command.Parameters.AddWithValue("@ReservationTime", string.IsNullOrEmpty(item.Time) ? DBNull.Value : (object)item.Time);

                        await command.ExecuteNonQueryAsync();
                        _logger.LogDebug("   ✅ Tétel beszúrva: {ItemName}, {Quantity}x {Price} Ft", item.Name, item.Quantity, item.Price);
                    }
                }

                await transaction.CommitAsync();
                _logger.LogInformation("✅ Rendelés sikeresen létrehozva: {OrderId}", orderId);

                return Ok(new { 
                    success = true, 
                    message = "Rendelés sikeresen rögzítve!",
                    orderId = orderId,
                    orderDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "❌ Tranzakció hiba rendelés létrehozásakor");
                return StatusCode(500, new { success = false, message = "Adatbázis hiba történt: " + ex.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 KRITIKUS HIBA a CreateOrder-ben");
            return StatusCode(500, new { success = false, message = "Hiba történt a rendelés feldolgozása során: " + ex.Message });
        }
    }

    // ==================== RENDELÉS KEZELÉSEK ====================

    [HttpPost("Approve")]
    public async Task<IActionResult> ApproveOrder([FromBody] OrderActionModel model)
    {
        _logger.LogInformation("🚀 ApproveOrder hívás: {OrderId}", model?.OrderId);
        
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

            if (string.IsNullOrEmpty(model?.OrderId))
            {
                return BadRequest(new { success = false, message = "Hiányzó rendelés azonosító" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'processing' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("✅ Rendelés elfogadva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elfogadva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba a rendelés elfogadásakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elfogadása során" });
        }
    }

    [HttpPost("Reject")]
    public async Task<IActionResult> RejectOrder([FromBody] OrderActionModel model)
    {
        _logger.LogInformation("🚀 RejectOrder hívás: {OrderId}", model?.OrderId);
        
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

            if (string.IsNullOrEmpty(model?.OrderId))
            {
                return BadRequest(new { success = false, message = "Hiányzó rendelés azonosító" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'rejected' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("✅ Rendelés elutasítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elutasítva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba a rendelés elutasításakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elutasítása során" });
        }
    }

    [HttpPost("MarkDelivered")]
    public async Task<IActionResult> MarkDelivered([FromBody] OrderActionModel model)
    {
        _logger.LogInformation("🚀 MarkDelivered hívás: {OrderId}", model?.OrderId);
        
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

            if (string.IsNullOrEmpty(model?.OrderId))
            {
                return BadRequest(new { success = false, message = "Hiányzó rendelés azonosító" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'delivered' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("✅ Rendelés kiszállítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen kiszállítva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba a rendelés kiszállítás jelölésénél");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés kiszállítás jelölése során" });
        }
    }

    // ==================== MODELLEK ====================

    public class OrderActionModel
    {
        public string OrderId { get; set; } = string.Empty;
    }

    public class OrderModel
    {
        public string UserId { get; set; } = string.Empty;
        public List<OrderItemModel>? Items { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal ServiceFee { get; set; }
        public string? ReservationId { get; set; }
        public string? Notes { get; set; }
        public string? PaymentMethod { get; set; }
    }

    public class OrderItemModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int Quantity { get; set; }
        public string? Consumption { get; set; }
        public string? Date { get; set; }
        public string? Time { get; set; }
    }
}
