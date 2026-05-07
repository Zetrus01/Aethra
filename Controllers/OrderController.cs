// OrderController.cs - TELJESEN TISZTA, MŰKÖDŐ VERZIÓ
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Dynamic;
using MySql.Data.MySqlClient;
using System.Data.Common;

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

            using var command = connection.CreateCommand();
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

    private string SafeGetString(DbDataReader reader, string columnName)
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

    private long SafeGetLong(DbDataReader reader, string columnName)
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

    private int SafeGetInt(DbDataReader reader, string columnName)
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

    // ==================== FELHASZNÁLÓ RENDELÉSEI ====================

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

            using var command = connection.CreateCommand();
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
            using (var reader = await command.ExecuteReaderAsync())
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
                    order.Notes = SafeGetString(reader, "Notes");
                    order.CreatedAt = SafeGetString(reader, "CreatedAt");
                    order.PaymentMethod = SafeGetString(reader, "PaymentMethod");
                    
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

                    orders.Add(order);
                }
            }

            // Tételek betöltése
            foreach (dynamic order in orders)
            {
                order.Items = await GetOrderItemsAsync(order.OrderId);
            }

            return Ok(new { success = true, orders = orders });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a GetUserOrders-ben");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor", error = ex.Message });
        }
    }

    // ==================== ADMIN RENDELÉSEK ====================

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

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*,
                       (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount
                FROM Orders o
                ORDER BY o.OrderDate DESC, o.CreatedAt DESC";

            var orders = new List<dynamic>();
            using (var reader = await command.ExecuteReaderAsync())
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
            _logger.LogError(ex, "Hiba a GetAllOrders-ben");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    // ==================== TÉTELEK LEKÉRDEZÉSE ====================

    private async Task<List<dynamic>> GetOrderItemsAsync(string orderId)
    {
        var items = new List<dynamic>();
        
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT * FROM OrderItems 
                WHERE OrderId = @OrderId 
                ORDER BY ItemName";

            command.Parameters.AddWithValue("@OrderId", orderId);

            using (var reader = await command.ExecuteReaderAsync())
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a tételek lekérdezésekor: {OrderId}", orderId);
        }

        return items;
    }

    // ==================== ADMIN MŰVELETEK ====================

    [HttpPost("ApproveOrder")]
    public async Task<IActionResult> ApproveOrder([FromBody] OrderActionModel model)
    {
        try
        {
            _logger.LogInformation("ApproveOrder hívás: {OrderId}", model?.OrderId);
            
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

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'processing' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Rendelés elfogadva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elfogadva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés elfogadásakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elfogadása során" });
        }
    }

    [HttpPost("RejectOrder")]
    public async Task<IActionResult> RejectOrder([FromBody] OrderActionModel model)
    {
        try
        {
            _logger.LogInformation("RejectOrder hívás: {OrderId}", model?.OrderId);
            
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

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'rejected' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Rendelés elutasítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elutasítva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés elutasításakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elutasítása során" });
        }
    }

    [HttpPost("MarkOrderDelivered")]
    public async Task<IActionResult> MarkOrderDelivered([FromBody] OrderActionModel model)
    {
        try
        {
            _logger.LogInformation("MarkOrderDelivered hívás: {OrderId}", model?.OrderId);
            
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

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Orders SET Status = 'delivered' WHERE OrderId = @OrderId";
            command.Parameters.AddWithValue("@OrderId", model.OrderId);

            var affectedRows = await command.ExecuteNonQueryAsync();
            
            if (affectedRows > 0)
            {
                _logger.LogInformation("Rendelés kiszállítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen kiszállítva" });
            }
            
            return NotFound(new { success = false, message = "Rendelés nem található" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés kiszállítás jelölésénél");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés kiszállítás jelölése során" });
        }
    }

    // ==================== RENDELÉS LÉTREHOZÁS ====================

    private string GenerateOrderId()
    {
        return "ORD" + DateTime.Now.ToString("yyyyMMddHHmmss") + new Random().Next(1000, 9999).ToString();
    }

    [HttpPost("CreateOrder")]
    public async Task<IActionResult> CreateOrder([FromBody] OrderModel model)
    {
        try
        {
            _logger.LogInformation("CreateOrder hívás: UserId={UserId}, Items={ItemCount}", 
                model?.UserId, model?.Items?.Count ?? 0);

            if (model == null || string.IsNullOrWhiteSpace(model.UserId))
            {
                return BadRequest(new { success = false, message = "Hiányzó felhasználói adatok." });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                string orderId = GenerateOrderId();
                
                using var command = connection.CreateCommand();
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

                if (model.Items != null)
                {
                    foreach (var item in model.Items)
                    {
                        using var itemCommand = connection.CreateCommand();
                        itemCommand.CommandText = @"
                            INSERT INTO OrderItems (OrderId, ItemName, ItemDescription, Quantity, 
                                                   UnitPrice, TotalPrice, ConsumptionType, 
                                                   ReservationDate, ReservationTime)
                            VALUES (@OrderId, @ItemName, @ItemDescription, @Quantity, 
                                    @UnitPrice, @TotalPrice, @ConsumptionType, 
                                    @ReservationDate, @ReservationTime)";

                        itemCommand.Parameters.AddWithValue("@OrderId", orderId);
                        itemCommand.Parameters.AddWithValue("@ItemName", item.Name);
                        itemCommand.Parameters.AddWithValue("@ItemDescription", item.Description ?? string.Empty);
                        itemCommand.Parameters.AddWithValue("@Quantity", item.Quantity);
                        itemCommand.Parameters.AddWithValue("@UnitPrice", (long)item.Price);
                        itemCommand.Parameters.AddWithValue("@TotalPrice", (long)(item.Price * item.Quantity));
                        itemCommand.Parameters.AddWithValue("@ConsumptionType", item.Consumption ?? "restaurant");
                        itemCommand.Parameters.AddWithValue("@ReservationDate", string.IsNullOrEmpty(item.Date) ? DBNull.Value : (object)item.Date);
                        itemCommand.Parameters.AddWithValue("@ReservationTime", string.IsNullOrEmpty(item.Time) ? DBNull.Value : (object)item.Time);

                        await itemCommand.ExecuteNonQueryAsync();
                    }
                }

                await transaction.CommitAsync();

                _logger.LogInformation("Rendelés létrehozva: {OrderId}", orderId);

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
                _logger.LogError(ex, "Tranzakció hiba");
                return StatusCode(500, new { success = false, message = "Adatbázis hiba történt: " + ex.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a CreateOrder-ben");
            return StatusCode(500, new { success = false, message = "Hiba történt: " + ex.Message });
        }
    }

    // ==================== EGÉSZSÉG ELLENŐRZÉS ====================
    
    [HttpGet("Health")]
    public IActionResult Health()
    {
        try
        {
            using var connection = new MySqlConnection(_connectionString);
            connection.Open();
            connection.Close();
            
            return Ok(new { status = "healthy", timestamp = DateTime.Now });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check hiba");
            return StatusCode(500, new { status = "unhealthy", error = ex.Message });
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
        public DeliveryAddressModel? DeliveryAddress { get; set; }
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

    public class DeliveryAddressModel
    {
        public string Street { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Zip { get; set; } = string.Empty;
        public string? Floor { get; set; }
        public string? Notes { get; set; }
    }
}
