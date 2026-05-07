// OrderController.cs - TELJES JAVÍTOTT VERZIÓ
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Dynamic;
using System.Linq;
using MySql.Data.MySqlClient;

[Route("[controller]")]
[ApiController]
public class OrderController : ControllerBase
{
    private readonly string? _connectionString;
    private readonly ILogger<OrderController> _logger;

    public OrderController(IConfiguration configuration, ILogger<OrderController> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _logger = logger;
    }

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

            // Felhasználónév lekérése sessionból
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
                    order.OrderDate = reader.GetString(reader.GetOrdinal("OrderDate"));
                    order.TotalPrice = reader.GetInt64(reader.GetOrdinal("TotalPrice"));
                    order.Status = reader.GetString(reader.GetOrdinal("Status"));
                    order.ItemsCount = reader.GetInt32(reader.GetOrdinal("ItemsCount"));
                    order.ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee"));
                    order.ReservationId = !reader.IsDBNull(reader.GetOrdinal("ReservationId")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationId")) : null;
                    
                    // Asztalfoglalás adatok
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        order.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            TableNumber = !reader.IsDBNull(reader.GetOrdinal("ReservationTableNumber")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationTableNumber")) : null,
                            Date = reader.GetString(reader.GetOrdinal("ReservationDate")),
                            Time = reader.GetString(reader.GetOrdinal("ReservationTime")),
                            Guests = reader.GetInt32(reader.GetOrdinal("ReservationGuests"))
                        };
                    }
                    
                    order.Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                                 reader.GetString(reader.GetOrdinal("Notes")) : null;
                    order.CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt"));
                    
                    orders.Add(order);
                }
            }

            // Rendelés tételeinek betöltése
            foreach (dynamic order in orders)
            {
                order.Items = await GetOrderItemsAsync(order.OrderId);
            }

            return Ok(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a felhasználó rendeléseinek lekérdezésekor");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
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

            // Ellenőrizzük, hogy admin-e
            var isAdmin = await IsUserAdminAsync(sessionId);
            if (!isAdmin)
            {
                return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*, u.Email as UserEmail,
                       (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount,
                       r.TableName as ReservationTableName,
                       r.TableNumber as ReservationTableNumber,
                       r.Date as ReservationDate,
                       r.Time as ReservationTime,
                       r.Guests as ReservationGuests,
                       r.TableLocation as ReservationTableLocation
                FROM Orders o
                LEFT JOIN User u ON o.UserId = u.UserName
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
                    order.UserEmail = !reader.IsDBNull(reader.GetOrdinal("UserEmail")) ? 
                                     reader.GetString(reader.GetOrdinal("UserEmail")) : null;
                    order.OrderDate = reader.GetString(reader.GetOrdinal("OrderDate"));
                    order.TotalPrice = reader.GetInt64(reader.GetOrdinal("TotalPrice"));
                    order.Status = reader.GetString(reader.GetOrdinal("Status"));
                    order.ItemsCount = reader.GetInt32(reader.GetOrdinal("ItemsCount"));
                    order.ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee"));
                    order.ReservationId = !reader.IsDBNull(reader.GetOrdinal("ReservationId")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationId")) : null;
                    
                    // Asztalfoglalás adatok
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        order.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            TableNumber = !reader.IsDBNull(reader.GetOrdinal("ReservationTableNumber")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationTableNumber")) : null,
                            Date = reader.GetString(reader.GetOrdinal("ReservationDate")),
                            Time = reader.GetString(reader.GetOrdinal("ReservationTime")),
                            Guests = reader.GetInt32(reader.GetOrdinal("ReservationGuests")),
                            TableLocation = !reader.IsDBNull(reader.GetOrdinal("ReservationTableLocation")) ? 
                                          reader.GetString(reader.GetOrdinal("ReservationTableLocation")) : null
                        };
                    }
                    
                    order.Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                                 reader.GetString(reader.GetOrdinal("Notes")) : null;
                    order.CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt"));
                    order.DeliveryAddress = !reader.IsDBNull(reader.GetOrdinal("DeliveryAddress")) ? 
                    reader.GetString(reader.GetOrdinal("DeliveryAddress")) : null;
                                order.PaymentMethod = !reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? 
                                    reader.GetString(reader.GetOrdinal("PaymentMethod")) : "card";
                    orders.Add(order);
                }
            }

            // Rendelés tételeinek betöltése
            foreach (dynamic order in orders)
            {
                order.Items = await GetOrderItemsAsync(order.OrderId);
            }

            return Ok(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba az összes rendelés lekérdezésekor");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    // **ÚJ: Felhasználó számláinak lekérdezése**
    [HttpGet("GetUserInvoices")]
    public async Task<IActionResult> GetUserInvoices()
    {
        try
        {
            var sessionId = HttpContext.Request.Cookies["SessionID"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { success = false, message = "Nincs érvényes session" });
            }

            // Felhasználónév lekérése sessionból
            var userName = await GetUserNameFromSessionAsync(sessionId);
            if (string.IsNullOrEmpty(userName))
            {
                return Unauthorized(new { success = false, message = "Érvénytelen session" });
            }

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    o.OrderId as InvoiceNumber,
                    o.OrderDate as InvoiceDate,
                    o.TotalPrice as TotalAmount,
                    o.Status,
                    o.ServiceFee,
                    (SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.OrderId) as ItemCount,
                    o.Notes,
                    o.ReservationId,
                    r.TableName as ReservationTableName,
                    r.Date as ReservationDate,
                    r.Time as ReservationTime
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                WHERE o.UserId = @UserId
                GROUP BY o.OrderId
                ORDER BY o.OrderDate DESC";

            command.Parameters.AddWithValue("@UserId", userName);

            var invoices = new List<dynamic>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dynamic invoice = new ExpandoObject();
                    invoice.InvoiceNumber = reader.GetString(reader.GetOrdinal("InvoiceNumber"));
                    invoice.InvoiceDate = reader.GetString(reader.GetOrdinal("InvoiceDate"));
                    invoice.TotalAmount = reader.GetInt64(reader.GetOrdinal("TotalAmount"));
                    invoice.Status = reader.GetString(reader.GetOrdinal("Status"));
                    invoice.ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee"));
                    invoice.ItemCount = reader.GetInt32(reader.GetOrdinal("ItemCount"));
                    invoice.Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                                   reader.GetString(reader.GetOrdinal("Notes")) : null;
                    invoice.ReservationId = !reader.IsDBNull(reader.GetOrdinal("ReservationId")) ? 
                                           reader.GetString(reader.GetOrdinal("ReservationId")) : null;
                    
                    // Asztalfoglalás adatok
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        invoice.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            Date = reader.GetString(reader.GetOrdinal("ReservationDate")),
                            Time = reader.GetString(reader.GetOrdinal("ReservationTime"))
                        };
                    }
                    
                    // Tételek betöltése
                    invoice.Items = await GetOrderItemsAsync(invoice.InvoiceNumber);

                    invoices.Add(invoice);
                }
            }

            return Ok(invoices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a felhasználó számláinak lekérdezésekor");
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    // **ÚJ: Számla részletek lekérdezése**
    [HttpGet("GetInvoiceDetails/{orderId}")]
    public async Task<IActionResult> GetInvoiceDetails(string orderId)
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

            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT 
                    o.*,
                    r.TableName as ReservationTableName,
                    r.TableNumber as ReservationTableNumber,
                    r.TableLocation as ReservationTableLocation,
                    r.Date as ReservationDate,
                    r.Time as ReservationTime,
                    r.Guests as ReservationGuests,
                    r.Message as ReservationMessage
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                WHERE o.OrderId = @OrderId";

            command.Parameters.AddWithValue("@OrderId", orderId);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Ellenőrizzük, hogy a felhasználóé-e a rendelés
                    var orderUserId = reader.GetString(reader.GetOrdinal("UserId"));
                    
                    // Admin ellenőrzése
                    var isAdmin = await IsUserAdminAsync(sessionId);
                    
                    if (orderUserId != userName && !isAdmin)
                    {
                        return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
                    }

                    dynamic invoice = new ExpandoObject();
                    
                    invoice.OrderId = reader.GetString(reader.GetOrdinal("OrderId"));
                    invoice.UserId = reader.GetString(reader.GetOrdinal("UserId"));
                    invoice.UserName = reader.GetString(reader.GetOrdinal("UserName"));
                    invoice.OrderDate = reader.GetString(reader.GetOrdinal("OrderDate"));
                    invoice.TotalAmount = reader.GetInt64(reader.GetOrdinal("TotalPrice"));
                    invoice.Status = reader.GetString(reader.GetOrdinal("Status"));
                    invoice.ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee"));
                    invoice.Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                                   reader.GetString(reader.GetOrdinal("Notes")) : null;
                    invoice.ReservationId = !reader.IsDBNull(reader.GetOrdinal("ReservationId")) ? 
                                           reader.GetString(reader.GetOrdinal("ReservationId")) : null;
                    
                    // Fizetési mód és szállítási cím
                    invoice.PaymentMethod = !reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? 
                                          reader.GetString(reader.GetOrdinal("PaymentMethod")) : "card";
                    
                    if (!reader.IsDBNull(reader.GetOrdinal("DeliveryAddress")))
                    {
                        try
                        {
                            var deliveryAddressJson = reader.GetString(reader.GetOrdinal("DeliveryAddress"));
                            invoice.DeliveryAddress = JsonSerializer.Deserialize<dynamic>(deliveryAddressJson);
                        }
                        catch
                        {
                            invoice.DeliveryAddress = null;
                        }
                    }
                    
                    // Asztalfoglalás adatok
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        invoice.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            TableNumber = !reader.IsDBNull(reader.GetOrdinal("ReservationTableNumber")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationTableNumber")) : null,
                            TableLocation = !reader.IsDBNull(reader.GetOrdinal("ReservationTableLocation")) ? 
                                          reader.GetString(reader.GetOrdinal("ReservationTableLocation")) : null,
                            Date = reader.GetString(reader.GetOrdinal("ReservationDate")),
                            Time = reader.GetString(reader.GetOrdinal("ReservationTime")),
                            Guests = reader.GetInt32(reader.GetOrdinal("ReservationGuests")),
                            Message = !reader.IsDBNull(reader.GetOrdinal("ReservationMessage")) ? 
                                     reader.GetString(reader.GetOrdinal("ReservationMessage")) : null
                        };
                    }
                    
                    // Tételek betöltése
                    invoice.Items = await GetOrderItemsAsync(orderId);

                    return Ok(invoice);
                }
                else
                {
                    return NotFound(new { success = false, message = "Számla nem található" });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a számla részletek lekérdezésekor: {OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "Hiba az adatok lekérdezésekor" });
        }
    }

    // **ÚJ: Számla PDF generálása/letöltése**
    [HttpGet("DownloadInvoice/{orderId}")]
    public async Task<IActionResult> DownloadInvoice(string orderId)
    {
        try
        {
            _logger.LogInformation("Számla letöltése kérés: {OrderId}", orderId);
            
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

            // Rendelés adatainak lekérdezése
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT o.*, u.Email as UserEmail
                FROM Orders o
                LEFT JOIN User u ON o.UserId = u.UserName
                WHERE o.OrderId = @OrderId";

            command.Parameters.AddWithValue("@OrderId", orderId);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Ellenőrizzük, hogy a felhasználóé-e a rendelés
                    var orderUserId = reader.GetString(reader.GetOrdinal("UserId"));
                    
                    // Admin ellenőrzése
                    var isAdmin = await IsUserAdminAsync(sessionId);
                    
                    if (orderUserId != userName && !isAdmin)
                    {
                        return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
                    }

                    // Rendelés adatai
                    var orderData = new
                    {
                        OrderId = reader.GetString(reader.GetOrdinal("OrderId")),
                        UserName = reader.GetString(reader.GetOrdinal("UserName")),
                        UserEmail = !reader.IsDBNull(reader.GetOrdinal("UserEmail")) ? 
                                   reader.GetString(reader.GetOrdinal("UserEmail")) : null,
                        OrderDate = reader.GetString(reader.GetOrdinal("OrderDate")),
                        TotalPrice = reader.GetInt64(reader.GetOrdinal("TotalPrice")),
                        Status = reader.GetString(reader.GetOrdinal("Status")),
                        ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee")),
                        PaymentMethod = !reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? 
                                      reader.GetString(reader.GetOrdinal("PaymentMethod")) : "card",
                        Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                               reader.GetString(reader.GetOrdinal("Notes")) : null
                    };

                    // Tételek lekérdezése
            var items = await GetOrderItemsAsync(orderId);

            // Visszatérünk JSON adattal, a frontend majd HTML számlát generál
            return Ok(new { 
                success = true, 
                message = "Számla adatok",
                order = orderData,
                items = items,
                downloadType = "html_generation",
                timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });
                }
                else
                {
                    return NotFound(new { success = false, message = "Rendelés nem található" });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a számla letöltésekor: {OrderId}", orderId);
            return StatusCode(500, new { success = false, message = "Hiba a számla letöltésekor" });
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
                item.ItemDescription = !reader.IsDBNull(reader.GetOrdinal("ItemDescription")) ? 
                                     reader.GetString(reader.GetOrdinal("ItemDescription")) : null;
                item.Quantity = reader.GetInt32(reader.GetOrdinal("Quantity"));
                item.UnitPrice = reader.GetInt64(reader.GetOrdinal("UnitPrice"));
                item.TotalPrice = reader.GetInt64(reader.GetOrdinal("TotalPrice"));
                item.ConsumptionType = !reader.IsDBNull(reader.GetOrdinal("ConsumptionType")) ? 
                                     reader.GetString(reader.GetOrdinal("ConsumptionType")) : "restaurant";
                item.ReservationDate = !reader.IsDBNull(reader.GetOrdinal("ReservationDate")) ? 
                                     reader.GetString(reader.GetOrdinal("ReservationDate")) : null;
                item.ReservationTime = !reader.IsDBNull(reader.GetOrdinal("ReservationTime")) ? 
                                     reader.GetString(reader.GetOrdinal("ReservationTime")) : null;
                
                items.Add(item);
            }
        }

        return items;
    }

    // Segédmetódus oszlop létezésének ellenőrzéséhez
    private bool HasColumn(System.Data.Common.DbDataReader reader, string columnName)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    [HttpPost("Approve")]
    public async Task<IActionResult> ApproveOrder([FromBody] OrderActionModel model)
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

            if (string.IsNullOrEmpty(model.OrderId))
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
                _logger.LogInformation("Rendelés elfogadva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elfogadva" });
            }
            else
            {
                return NotFound(new { success = false, message = "Rendelés nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés elfogadásakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elfogadása során" });
        }
    }

    [HttpPost("Reject")]
    public async Task<IActionResult> RejectOrder([FromBody] OrderActionModel model)
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

            if (string.IsNullOrEmpty(model.OrderId))
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
                _logger.LogInformation("Rendelés elutasítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen elutasítva" });
            }
            else
            {
                return NotFound(new { success = false, message = "Rendelés nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés elutasításakor");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés elutasítása során" });
        }
    }

    [HttpPost("MarkDelivered")]
    public async Task<IActionResult> MarkDelivered([FromBody] OrderActionModel model)
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

            if (string.IsNullOrEmpty(model.OrderId))
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
                _logger.LogInformation("Rendelés kiszállítva: {OrderId}", model.OrderId);
                return Ok(new { success = true, message = "Rendelés sikeresen kiszállítva" });
            }
            else
            {
                return NotFound(new { success = false, message = "Rendelés nem található" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba a rendelés kiszállítás jelölésénél");
            return StatusCode(500, new { success = false, message = "Hiba a rendelés kiszállítás jelölése során" });
        }
    }

[HttpPost("CreateOrder")]
public async Task<IActionResult> CreateOrder([FromBody] OrderModel model)
{
    try
    {
        _logger.LogInformation("Új rendelés létrehozása: {UserId}, Ételek száma: {ItemCount}, Asztalfoglalás: {ReservationId}, Fizetés: {PaymentMethod}, Szállítás: {HasDelivery}", 
            model.UserId, model.Items?.Count ?? 0, model.ReservationId ?? "Nincs", 
            model.PaymentMethod ?? "card", model.DeliveryAddress != null ? "Igen" : "Nem");

        // Validáció
        if (model == null || string.IsNullOrWhiteSpace(model.UserId))
        {
            return BadRequest(new { success = false, message = "Hiányzó felhasználói adatok." });
        }

        // **JAVÍTÁS: Ha van asztalfoglalás, akkor lehet üres a rendelés**
        bool hasReservation = !string.IsNullOrEmpty(model.ReservationId);
        bool hasItems = model.Items != null && model.Items.Count > 0;

        // Ha nincs asztalfoglalás ÉS nincsenek ételek, akkor hiba
        if (!hasReservation && !hasItems)
        {
            return BadRequest(new { success = false, message = "A rendelés üres. Adj ételeket a kosárhoz, vagy foglalj asztalt!" });
        }

        // Ha nincsenek ételek, de van asztalfoglalás, akkor létrehozunk egy placeholder item-et
        if (!hasItems && hasReservation)
        {
            _logger.LogInformation("📅 Csak asztalfoglalásos rendelés, placeholder item létrehozása...");
            
            // Lekérjük a foglalás adatait
            string? reservationDetails = null;
            await using (var conn = new MySql.Data.MySqlClient.MySqlConnection(_connectionString))
            {
                await conn.OpenAsync();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT TableName, TableNumber, Date, Time, Guests 
                    FROM Reservations 
                    WHERE ReservationId = @ReservationId";
                cmd.Parameters.AddWithValue("@ReservationId", model.ReservationId);
                
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var tableName = reader.GetString(0);
                        var date = reader.GetString(2);
                        var time = reader.GetString(3);
                        var guests = reader.GetInt32(4);
                        reservationDetails = $"{tableName} - {date} {time} ({guests} fő)";
                    }
                }
            }
            
            // Placeholder item létrehozása
            model.Items = new List<OrderItemModel>
            {
                new OrderItemModel
                {
                    Name = "Asztalfoglalás",
                    Description = reservationDetails ?? "Asztalfoglalás",
                    Price = 0,
                    Quantity = 1,
                    Consumption = "restaurant"
                }
            };
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            // 1. Rendelés létrehozása
            string orderId = GenerateOrderId();
            long totalPrice = (long)model.TotalAmount;
            long serviceFee = (long)model.ServiceFee;
            
            // Fizetési mód (alapértelmezett: bankkártya)
            string paymentMethod = model.PaymentMethod ?? "card";
            
            // Szállítási cím JSON formátumban
            string? deliveryAddressJson = null;
            if (model.DeliveryAddress != null)
            {
                deliveryAddressJson = JsonSerializer.Serialize(model.DeliveryAddress);
                _logger.LogInformation("Szállítási cím mentése: {Address}", deliveryAddressJson);
            }
            var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO Orders (OrderId, UserId, UserName, OrderDate, TotalPrice, Status, 
                                    ServiceFee, ItemsCount, ReservationId, Notes, 
                                    PaymentMethod, DeliveryAddress, CreatedAt)
                VALUES (@OrderId, @UserId, @UserName, @OrderDate, @TotalPrice, @Status, 
                        @ServiceFee, @ItemsCount, @ReservationId, @Notes, 
                        @PaymentMethod, @DeliveryAddress, NOW())";

            command.Parameters.AddWithValue("@OrderId", orderId);
            command.Parameters.AddWithValue("@UserId", model.UserId);
            command.Parameters.AddWithValue("@UserName", model.UserId);
            command.Parameters.AddWithValue("@OrderDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            command.Parameters.AddWithValue("@TotalPrice", totalPrice);
            command.Parameters.AddWithValue("@Status", "pending");
            command.Parameters.AddWithValue("@ServiceFee", serviceFee);
            command.Parameters.AddWithValue("@ItemsCount", model.Items.Count);
            command.Parameters.AddWithValue("@PaymentMethod", paymentMethod);
            
            if (string.IsNullOrEmpty(model.ReservationId))
            {
                command.Parameters.AddWithValue("@ReservationId", DBNull.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@ReservationId", model.ReservationId);
            }
            
            if (string.IsNullOrEmpty(deliveryAddressJson))
            {
                command.Parameters.AddWithValue("@DeliveryAddress", DBNull.Value);
            }
            else
            {
                command.Parameters.AddWithValue("@DeliveryAddress", deliveryAddressJson);
            }
            
            command.Parameters.AddWithValue("@Notes", model.Notes ?? string.Empty);

            await command.ExecuteNonQueryAsync();

            // 2. Rendelés tételek hozzáadása
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
                
                if (string.IsNullOrEmpty(item.Date))
                {
                    command.Parameters.AddWithValue("@ReservationDate", DBNull.Value);
                }
                else
                {
                    command.Parameters.AddWithValue("@ReservationDate", item.Date);
                }
                
                if (string.IsNullOrEmpty(item.Time))
                {
                    command.Parameters.AddWithValue("@ReservationTime", DBNull.Value);
                }
                else
                {
                    command.Parameters.AddWithValue("@ReservationTime", item.Time);
                }

                await command.ExecuteNonQueryAsync();
            }

            // 3. Asztalfoglalás státusz frissítése (ha van)
            bool reservationUpdated = false;
            string? dbReservationId = null;
            string? reservationStatus = null;
            
            if (!string.IsNullOrEmpty(model.ReservationId))
            {
                // Ellenőrizzük, hogy létezik-e a foglalás
                var checkReservationCommand = connection.CreateCommand();
                checkReservationCommand.CommandText = @"
                    SELECT ReservationId, UserId, Status, TableName, Date, Time, TableNumber
                    FROM Reservations 
                    WHERE ReservationId = @ReservationId 
                    AND UserId = @UserId";
                
                checkReservationCommand.Parameters.AddWithValue("@ReservationId", model.ReservationId);
                checkReservationCommand.Parameters.AddWithValue("@UserId", model.UserId);

                string? reservationUserId = null;
                string? currentStatus = null;
                string? tableName = null;
                string? reservationDate = null;
                string? reservationTime = null;
                string? tableNumber = null;

                await using (var reader = await checkReservationCommand.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        dbReservationId = reader.GetString(reader.GetOrdinal("ReservationId"));
                        reservationUserId = reader.GetString(reader.GetOrdinal("UserId"));
                        currentStatus = reader.GetString(reader.GetOrdinal("Status"));
                        tableName = reader.GetString(reader.GetOrdinal("TableName"));
                        tableNumber = !reader.IsDBNull(reader.GetOrdinal("TableNumber")) ? 
                                     reader.GetString(reader.GetOrdinal("TableNumber")) : null;
                        reservationDate = reader.GetString(reader.GetOrdinal("Date"));
                        reservationTime = reader.GetString(reader.GetOrdinal("Time"));
                    }
                }

                if (dbReservationId != null && reservationUserId == model.UserId)
                {
                    // Frissítjük a foglalást
                    var updateReservationCommand = connection.CreateCommand();
                    updateReservationCommand.CommandText = @"
                        UPDATE Reservations 
                        SET Status = 'ordered', 
                            OrderId = @OrderId,
                            UpdatedAt = NOW()
                        WHERE ReservationId = @ReservationId 
                        AND UserId = @UserId";
                    
                    updateReservationCommand.Parameters.AddWithValue("@OrderId", orderId);
                    updateReservationCommand.Parameters.AddWithValue("@ReservationId", dbReservationId);
                    updateReservationCommand.Parameters.AddWithValue("@UserId", model.UserId);
                    
                    var affectedReservations = await updateReservationCommand.ExecuteNonQueryAsync();
                    
                    if (affectedReservations > 0)
                    {
                        reservationUpdated = true;
                        reservationStatus = "ordered";
                        
                        _logger.LogInformation(
                            "✅ Asztalfoglalás státusza frissítve: {ReservationId} -> ordered, OrderId: {OrderId}, " +
                            "Asztal: {TableName} (#{TableNumber}), Dátum: {Date} {Time}",
                            dbReservationId, orderId, tableName, tableNumber, reservationDate, reservationTime);
                    }
                }
            }

            await transaction.CommitAsync();

            _logger.LogInformation(
                "✅ Rendelés sikeresen létrehozva: {OrderId} - {TotalPrice} Ft, " +
                "Fizetési mód: {PaymentMethod}, Szállítási cím: {HasDeliveryAddress}, " +
                "Asztalfoglalás frissítve: {ReservationUpdated}, Felhasználó: {UserName}", 
                orderId, totalPrice, paymentMethod, deliveryAddressJson != null ? "Igen" : "Nem", 
                reservationUpdated, model.UserId);

            // 4. Válasz összeállítása
            var response = new
            {
                success = true,
                message = "Rendelés sikeresen rögzítve!",
                orderId = orderId,
                orderDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                paymentMethod = paymentMethod,
                hasDeliveryAddress = deliveryAddressJson != null,
                reservationUpdated = reservationUpdated,
                dbReservationId = dbReservationId,
                reservationStatus = reservationStatus
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "❌ Tranzakció hiba rendelés létrehozásakor");
            return StatusCode(500, new { 
                success = false, 
                message = "Adatbázis hiba történt a rendelés rögzítése során.",
                error = ex.Message
            });
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ Hiba rendelés létrehozásakor");
        return StatusCode(500, new { 
            success = false, 
            message = "Hiba történt a rendelés feldolgozása során.",
            error = ex.Message
        });
    }
}

    // **ÚJ: Email küldés**
    private async Task SendOrderConfirmationEmail(string orderId, string userName, OrderModel model)
    {
        try
        {
            // Felhasználó email címének lekérése
            var userEmail = await GetUserEmailAsync(userName);
            if (string.IsNullOrEmpty(userEmail))
            {
                _logger.LogWarning("Nem sikerült lekérni a felhasználó email címét: {UserName}", userName);
                return;
            }

            // Email adatok összeállítása
            var emailModel = new
            {
                UserName = userName,
                Email = userEmail,
                OrderId = orderId,
                Items = model.Items.Select(item => new
                {
                    Name = item.Name,
                    Quantity = item.Quantity,
                    Price = item.Price
                }).ToList(),
                TotalAmount = model.TotalAmount,
                ServiceFee = model.ServiceFee,
                ReservationId = model.ReservationId,
                Notes = model.Notes,
                PaymentMethod = model.PaymentMethod,
                DeliveryAddress = model.DeliveryAddress
            };

            // Email küldése
            var emailJson = JsonSerializer.Serialize(emailModel);
            
            // TODO: Valós email küldés implementálása
            _logger.LogInformation("Rendelés megerősítő email elküldve: {OrderId} -> {Email}", orderId, userEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba az email küldésekor");
        }
    }

    // **ÚJ: Email cím lekérdezése**
    private async Task<string?> GetUserEmailAsync(string userName)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT Email FROM User WHERE UserName = @UserName";
            command.Parameters.AddWithValue("@UserName", userName);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

    // **ÚJ: Order lekérdezése ID alapján**
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

            // Felhasználónév lekérése
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
                       r.Guests as ReservationGuests,
                       r.TableLocation as ReservationTableLocation,
                       r.Message as ReservationMessage
                FROM Orders o
                LEFT JOIN Reservations r ON o.ReservationId = r.ReservationId
                WHERE o.OrderId = @OrderId";

            command.Parameters.AddWithValue("@OrderId", orderId);

            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    // Ellenőrizzük, hogy a felhasználóé-e a rendelés
                    var orderUserId = reader.GetString(reader.GetOrdinal("UserId"));
                    
                    // Admin ellenőrzése
                    var isAdmin = await IsUserAdminAsync(sessionId);
                    
                    if (orderUserId != userName && !isAdmin)
                    {
                        return StatusCode(403, new { success = false, message = "Nincs jogosultság" });
                    }

                    dynamic order = new ExpandoObject();
                    
                    order.OrderId = reader.GetString(reader.GetOrdinal("OrderId"));
                    order.UserId = reader.GetString(reader.GetOrdinal("UserId"));
                    order.UserName = reader.GetString(reader.GetOrdinal("UserName"));
                    order.OrderDate = reader.GetString(reader.GetOrdinal("OrderDate"));
                    order.TotalPrice = reader.GetInt64(reader.GetOrdinal("TotalPrice"));
                    order.Status = reader.GetString(reader.GetOrdinal("Status"));
                    order.ServiceFee = reader.GetInt64(reader.GetOrdinal("ServiceFee"));
                    order.ReservationId = !reader.IsDBNull(reader.GetOrdinal("ReservationId")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationId")) : null;
                    
                    // Fizetési mód és szállítási cím
                    order.PaymentMethod = !reader.IsDBNull(reader.GetOrdinal("PaymentMethod")) ? 
                                        reader.GetString(reader.GetOrdinal("PaymentMethod")) : "card";
                    
                    if (!reader.IsDBNull(reader.GetOrdinal("DeliveryAddress")))
                    {
                        try
                        {
                            var deliveryAddressJson = reader.GetString(reader.GetOrdinal("DeliveryAddress"));
                            order.DeliveryAddress = JsonSerializer.Deserialize<dynamic>(deliveryAddressJson);
                        }
                        catch
                        {
                            order.DeliveryAddress = null;
                        }
                    }
                    
                    // Asztalfoglalás adatok
                    if (!reader.IsDBNull(reader.GetOrdinal("ReservationTableName")))
                    {
                        order.ReservationDetails = new
                        {
                            TableName = reader.GetString(reader.GetOrdinal("ReservationTableName")),
                            TableNumber = !reader.IsDBNull(reader.GetOrdinal("ReservationTableNumber")) ? 
                                         reader.GetString(reader.GetOrdinal("ReservationTableNumber")) : null,
                            Date = reader.GetString(reader.GetOrdinal("ReservationDate")),
                            Time = reader.GetString(reader.GetOrdinal("ReservationTime")),
                            Guests = reader.GetInt32(reader.GetOrdinal("ReservationGuests")),
                            TableLocation = !reader.IsDBNull(reader.GetOrdinal("ReservationTableLocation")) ? 
                                          reader.GetString(reader.GetOrdinal("ReservationTableLocation")) : null,
                            Message = !reader.IsDBNull(reader.GetOrdinal("ReservationMessage")) ? 
                                     reader.GetString(reader.GetOrdinal("ReservationMessage")) : null
                        };
                    }
                    
                    order.Notes = !reader.IsDBNull(reader.GetOrdinal("Notes")) ? 
                                 reader.GetString(reader.GetOrdinal("Notes")) : null;
                    order.CreatedAt = reader.GetString(reader.GetOrdinal("CreatedAt"));
                    
                    // Tételek betöltése
                    order.Items = await GetOrderItemsAsync(order.OrderId);

                    return Ok(order);
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

    // Segédmetódusok
    private string GenerateOrderId()
    {
        return "ORD" + DateTime.Now.ToString("yyyyMMddHHmmss") + 
               new Random().Next(1000, 9999).ToString();
    }

    private async Task<string?> GetUserNameByIdAsync(string userId)
    {
        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "SELECT UserName FROM User WHERE UserName = @UserId";
            command.Parameters.AddWithValue("@UserId", userId);

            var result = await command.ExecuteScalarAsync();
            return result?.ToString();
        }
        catch
        {
            return null;
        }
    }

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

            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT u.UserName
                FROM Session s
                JOIN User u ON s.UserName = u.UserName
                WHERE s.SessionID = @SessionId AND u.UserName = 'admin'";

            command.Parameters.AddWithValue("@SessionId", sessionId);

            var result = await command.ExecuteScalarAsync();
            return result != null && result.ToString() == "admin";
        }
        catch
        {
            return false;
        }
    }

    // Models - EZEK HIANYOTTAK!
    public class OrderActionModel
    {
        public string OrderId { get; set; } = string.Empty;
    }

    public class OrderModel
    {
        public string UserId { get; set; } = string.Empty;
        public List<OrderItemModel> Items { get; set; } = new List<OrderItemModel>();
        public decimal TotalAmount { get; set; }
        public decimal ServiceFee { get; set; }
        public string? ReservationId { get; set; }
        public string? Notes { get; set; }
        public string? PaymentMethod { get; set; }  // Új: Fizetési mód
        public DeliveryAddressModel? DeliveryAddress { get; set; }  // Új: Szállítási cím
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