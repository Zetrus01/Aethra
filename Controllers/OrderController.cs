// OrderController.cs - JAVÍTOTT VERZIÓ
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Dynamic;
using MySql.Data.MySqlClient;

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
    }

    // ==================== HELPER METÓDUSOK ====================

    private async Task<string?> GetUserNameFromSessionAsync(string sessionId)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT UserName FROM Session WHERE SessionID = @SessionId";
            command.Parameters.AddWithValue("@SessionId", sessionId);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a session lekérdezésekor");
            return null;
        }
    }

    private async Task<bool> IsUserAdminAsync(string sessionId)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT u.UserName
                FROM Session s
                JOIN User u ON s.UserName = u.UserName
                WHERE s.SessionID = @SessionId AND u.UserName = 'admin'";

            command.Parameters.AddWithValue("@SessionId", sessionId);

            var result = await command.ExecuteScalarAsync();
            return result != null;
        }
        catch
        {
            return false;
        }
    }

    private string GetCurrentUserId()
    {
        var sessionId = HttpContext.Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
            return null;
        
        // Itt lehetne cache-elni, de egyszerűség kedvéért közvetlenül lekérjük
        return GetUserNameFromSessionAsync(sessionId).GetAwaiter().GetResult();
    }

    // Biztonságos olvasás DateTime-ról string-re
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

    private long SafeGetLong(MySqlDataReader reader, string columnName)
    {
        try
        {
            var idx = reader.GetOrdinal(columnName);
            if (reader.IsDBNull(idx))
                return 0;
            
            return Convert.ToInt64(reader.GetValue(idx));
        }
        catch
        {
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
        catch
        {
            return 0;
        }
    }

    // ==================== API METÓDUSOK ====================

    [HttpGet("GetUserOrders")]
    public async Task<IActionResult> GetUserOrders()
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

            var orders = new List<dynamic>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
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
                    
                    // Asztalfoglalás adatok
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
                    
                    order.Notes = SafeGetString(reader, "Notes");
                    order.CreatedAt = SafeGetString(reader, "CreatedAt");
                    order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                    order.DeliveryAddress = SafeGetString(reader, "DeliveryAddress");

                    orders.Add(order);
                }
            }

            // Rendelés tételeinek betöltése
            foreach (dynamic order in orders)
            {
                order.Items = await GetOrderItemsAsync(order.OrderId);
            }

            return Ok(new { success = true, orders = orders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a felhasználó rendeléseinek lekérdezésekor");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor: " + ex.Message });
        }
    }

    private async Task<List<dynamic>> GetOrderItemsAsync(string orderId)
    {
        var items = new List<dynamic>();
        
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
            }
        }

        return items;
    }

    [HttpGet("GetAllOrders")]
    public async Task<IActionResult> GetAllOrders()
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

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*,
                       (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount,
                       r.TableName as ReservationTableName,
                       r.TableNumber as ReservationTableNumber,
                       r.Date as ReservationDate,
                       r.Time as ReservationTime
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                ORDER BY o.OrderDate DESC, o.CreatedAt DESC";

            var orders = new List<dynamic>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
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
                    order.CreatedAt = SafeGetString(reader, "CreatedAt");
                    order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                    
                    orders.Add(order);
                }
            }

            return Ok(new { success = true, orders = orders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba az összes rendelés lekérdezésekor");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    [HttpGet("GetOrder/{orderId}")]
    public async Task<IActionResult> GetOrder(string orderId)
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
                    
                    // Asztalfoglalás adatok
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
                    
                    // Tételek betöltése
                    order.Items = await GetOrderItemsAsync(orderId);

                    return Ok(new { success = true, order = order });
                }
                else
                {
                    return NotFound(new { success = false, message = "Rendelés nem található" });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés lekérdezésekor: {OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    // ==================== RENDELÉS LÉTREHOZÁS ====================

    private string GenerateOrderId()
    {
        return "ORD" + DateTime.Now.ToString("yyyyMMddHHmmss") + 
               new Random().Next(1000, 9999).ToString();
    }

    [HttpPost("CreateOrder")]
    public async Task<IActionResult> CreateOrder([FromBody] OrderModel model)
    {
        try
        {
            _logger.LogInformation("Új rendelés létrehozása: {UserId}, Ételek száma: {ItemCount}, Asztalfoglalás: {ReservationId}", 
                model.UserId, model.Items?.Count ?? 0, model.ReservationId ?? "Nincs");

            if (model == null || string.IsNullOrWhiteSpace(model.UserId))
            {
                return BadRequest(new { success = false, message = "Hiányzó felhasználói adatok." });
            }

            bool hasReservation = !string.IsNullOrEmpty(model.ReservationId);
            bool hasItems = model.Items != null && model.Items.Count > 0;

            if (!hasReservation && !hasItems)
            {
                return BadRequest(new { success = false, message = "A rendelés üres. Adj ételeket a kosárhoz, vagy foglalj asztalt!" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                string orderId = GenerateOrderId();
                
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
                command.Parameters.AddWithValue("@TotalPrice", (long)model.TotalAmount);
                command.Parameters.AddWithValue("@ServiceFee", (long)model.ServiceFee);
                command.Parameters.AddWithValue("@ItemsCount", model.Items?.Count ?? 1);
                command.Parameters.AddWithValue("@ReservationId", string.IsNullOrEmpty(model.ReservationId) ? DBNull.Value : (object)model.ReservationId);
                command.Parameters.AddWithValue("@Notes", model.Notes ?? string.Empty);
                command.Parameters.AddWithValue("@PaymentMethod", model.PaymentMethod ?? "card");

                await command.ExecuteNonQueryAsync();

                // Rendelés tételek hozzáadása
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
                return StatusCode(500, new { success = false, message = "Adatbázis hiba történt." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Hiba rendelés létrehozásakor");
            return StatusCode(500, new { success = false, message = "Hiba történt a rendelés feldolgozása során." });
        }
    }

    // ==================== MODELLEK ====================

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
