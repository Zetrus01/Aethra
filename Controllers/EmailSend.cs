// EmailSender.cs - TELJES EMAIL KÜLDÉSI KÓD JAVÍTVA
using Microsoft.AspNetCore.Mvc;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using Microsoft.Extensions.Configuration; // Ehhez kell a NuGet: Microsoft.Extensions.Configuration
using MySql.Data.MySqlClient;

[Route("Email")]
public class EmailController : Controller
{
    private readonly IConfiguration _configuration;
    private readonly string _smtpHost;
    private readonly int _smtpPort;
    private readonly string _smtpUsername;
    private readonly string _smtpPassword;
    private readonly string _senderName;

    // Konstruktor - itt kapjuk meg a konfigurációt
    public EmailController(IConfiguration configuration)
    {
        _configuration = configuration;
        _smtpHost = _configuration["EmailSettings:SmtpHost"] ?? "smtp.gmail.com";
        _smtpPort = int.Parse(_configuration["EmailSettings:SmtpPort"] ?? "587");
        _smtpUsername = _configuration["EmailSettings:SmtpUsername"] ?? "aethrarestaurant@gmail.com";
        _smtpPassword = _configuration["EmailSettings:SmtpPassword"] ?? "bowh bwja uygu pzbf";
        _senderName = _configuration["EmailSettings:SenderName"] ?? "AETHRA";
    }

    // 1. MEGLÉVŐ: RENDELÉS MEGERŐSÍTÉS
    [HttpPost("SendOrderConfirmation")]
    public async Task<IActionResult> SendOrderConfirmation()
    {
        try
        {
            Console.WriteLine($"📧 Rendelés megerősítő email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            OrderConfirmationModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<OrderConfirmationModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.OrderId = model.OrderId ?? "N/A";
            model.Items = model.Items ?? new List<OrderItem>();
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_senderName, _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"Rendelés megerősítés - #{model.OrderId}";
            
            // Email body generálása
            var emailBody = BuildOrderConfirmationEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Order confirmation email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send order confirmation email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Order confirmation email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Order confirmation email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // 2. FOGLALÁS ELFOGADÁSI EMAIL
    [HttpPost("SendReservationApproval")]
    public async Task<IActionResult> SendReservationApproval()
    {
        try
        {
            Console.WriteLine($"📧 Foglalás elfogadási email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            ReservationApprovalModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<ReservationApprovalModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.ReservationId = model.ReservationId ?? "N/A";
            model.TableName = model.TableName ?? "Asztal";
            model.Date = model.Date ?? DateTime.Now.ToString("yyyy.MM.dd.");
            model.Time = model.Time ?? "-";
            model.Guests = model.Guests ?? 1;
            model.TableLocation = model.TableLocation ?? "Éttermünkben";
            model.HtmlServices = model.HtmlServices ?? new List<string>();
            model.Notes = model.Notes ?? "";
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"✅ Foglalásodat elfogadtuk! - #{model.ReservationId}";
            
            // Email body generálása
            var emailBody = BuildReservationApprovalEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Reservation approval email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send reservation approval email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Foglalás elfogadás email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Reservation approval email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // 3. RENDELÉS ELFOGADÁSI EMAIL
    [HttpPost("SendOrderApproval")]
    public async Task<IActionResult> SendOrderApproval()
    {
        try
        {
            Console.WriteLine($"📧 Rendelés elfogadási email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            OrderApprovalModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<OrderApprovalModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.OrderId = model.OrderId ?? "N/A";
            model.Items = model.Items ?? new List<OrderItem>();
            model.EstimatedDelivery = model.EstimatedDelivery ?? "30-45 perc";
            model.OrderDate = model.OrderDate ?? DateTime.Now.ToString("yyyy.MM.dd. HH:mm");
            model.ReservationId = model.ReservationId ?? "";
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"✅ Rendelésed feldolgozás alatt! - #{model.OrderId}";
            
            // Email body generálása
            var emailBody = BuildOrderApprovalEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Order approval email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send order approval email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Rendelés elfogadás email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Order approval email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // 4. RENDELÉS KISZÁLLÍTÁSI EMAIL
    [HttpPost("SendOrderDelivered")]
    public async Task<IActionResult> SendOrderDelivered()
    {
        try
        {
            Console.WriteLine($"📧 Rendelés kiszállítási email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            OrderDeliveredModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<OrderDeliveredModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.OrderId = model.OrderId ?? "N/A";
            model.DeliveryTime = model.DeliveryTime ?? DateTime.Now.ToString("HH:mm");
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"🚚 Rendelésed kiszállítva! - #{model.OrderId}";
            
            // Email body generálása
            var emailBody = BuildOrderDeliveredEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Order delivered email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send order delivered email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Rendelés kiszállítás email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Order delivered email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // 5. FOGLALÁS ELUTASÍTÁSI EMAIL
    [HttpPost("SendReservationRejection")]
    public async Task<IActionResult> SendReservationRejection()
    {
        try
        {
            Console.WriteLine($"📧 Foglalás elutasítási email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            ReservationRejectionModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<ReservationRejectionModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.ReservationId = model.ReservationId ?? "N/A";
            model.TableName = model.TableName ?? "Asztal";
            model.Date = model.Date ?? DateTime.Now.ToString("yyyy.MM.dd.");
            model.Time = model.Time ?? "-";
            model.Guests = model.Guests ?? 1;
            model.TableLocation = model.TableLocation ?? "Éttermünkben";
            model.RejectionDate = model.RejectionDate ?? DateTime.Now.ToString("yyyy.MM.dd.");
            model.RejectionReason = model.RejectionReason ?? "Sajnos az asztal foglalás nem teljesíthető a kért időpontban.";
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"❌ Foglalásodat elutasítottuk - #{model.ReservationId}";
            
            // Email body generálása
            var emailBody = BuildReservationRejectionEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Reservation rejection email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send reservation rejection email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Foglalás elutasítás email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Reservation rejection email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // 6. RENDELÉS ELUTASÍTÁSI EMAIL
    [HttpPost("SendOrderRejection")]
    public async Task<IActionResult> SendOrderRejection()
    {
        try
        {
            Console.WriteLine($"📧 Rendelés elutasítási email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            OrderRejectionModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<OrderRejectionModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Vendég";
            model.Email = model.Email ?? "no-email@example.com";
            model.OrderId = model.OrderId ?? "N/A";
            model.RejectionDate = model.RejectionDate ?? DateTime.Now.ToString("yyyy.MM.dd.");
            model.OrderDate = model.OrderDate ?? DateTime.Now.ToString("yyyy.MM.dd. HH:mm");
            model.RejectionReason = model.RejectionReason ?? "A rendelést sajnos nem tudtuk teljesíteni.";
            
            // Email üzenet létrehozása
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"❌ Rendelésedet elutasítottuk - #{model.OrderId}";
            
            // Email body generálása
            var emailBody = BuildOrderRejectionEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Order rejection email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send order rejection email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Rendelés elutasítás email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Order rejection email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // KÖZÖS SMTP KÜLDÉSI METÓDUS
    private async Task<bool> SendEmailAsync(MimeMessage message)
    {
        try
        {
            using var client = new SmtpClient();
            
            // Csatlakozás
            await client.ConnectAsync(_smtpHost, _smtpPort, SecureSocketOptions.StartTls);
            
            // Authentikáció
            await client.AuthenticateAsync(_smtpUsername, _smtpPassword);
            
            // Küldés
            await client.SendAsync(message);
            
            // Szétkapcsolás
            await client.DisconnectAsync(true);
            
            return true;
        }
        catch (Exception smtpEx)
        {
            Console.WriteLine($"❌ SMTP error: {smtpEx.Message}");
            return false;
        }
    }
    
    // FOGLALÁS ELFOGADÁSI EMAIL
    private string BuildReservationApprovalEmail(ReservationApprovalModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Vendég";
            var reservationId = model.ReservationId ?? "N/A";
            var tableName = model.TableName ?? "Asztal";
            var tableNumber = model.TableNumber ?? "";
            var date = model.Date ?? DateTime.Now.ToString("yyyy.MM.dd.");
            var time = model.Time ?? "-";
            var guests = model.Guests?.ToString() ?? "1";
            var location = model.TableLocation ?? "Éttermünkben";
            var notes = model.Notes ?? "";
            var currentDate = DateTime.Now.ToString("yyyy.MM.dd. HH:mm");
            
            var fullTableInfo = !string.IsNullOrEmpty(tableNumber) 
                ? $"{tableName} (szám: {tableNumber})" 
                : tableName;
            
            // Szolgáltatások listájának építése
            string servicesHtml = "";
            if (model.HtmlServices != null && model.HtmlServices.Any())
            {
                servicesHtml = "<div style='background: #f8fafc; border-radius: 6px; padding: 15px; margin: 15px 0;'>";
                foreach (var service in model.HtmlServices)
                {
                    servicesHtml += $"<div style='padding: 8px 0; border-bottom: 1px solid #e2e8f0;'><span style='color: #10b981; margin-right: 8px;'>✓</span> {service}</div>";
                }
                servicesHtml += "</div>";
            }
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Foglalás megerősítés</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #10b981 0%, #059669 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #f0fdf4;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #10b981;
        }}
        .detail-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 15px;
            margin: 20px 0;
        }}
        .detail-item {{
            background: #f8fafc;
            padding: 15px;
            border-radius: 6px;
            border: 1px solid #e2e8f0;
        }}
        .detail-label {{
            font-weight: 600;
            color: #475569;
            font-size: 0.9rem;
            margin-bottom: 5px;
        }}
        .detail-value {{
            font-size: 1.1rem;
            font-weight: 700;
            color: #1e293b;
        }}
        .status-badge {{
            background: #10b981;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
        .important-note {{
            background: #fef3c7;
            border: 1px solid #f59e0b;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Foglalásodat elfogadtuk!</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <p><span class='status-badge'>ELFOGADVA</span></p>
                <p><strong>Foglalási azonosító:</strong> #{reservationId}</p>
                <p><strong>Megerősítés dátuma:</strong> {currentDate}</p>
            </div>
            
            <p>Örömmel értesítjük, hogy asztalfoglalásodat elfogadtuk! Az alábbiakban találod a foglalás részleteit:</p>
            
            <div class='detail-grid'>
                <div class='detail-item'>
                    <div class='detail-label'>Asztal</div>
                    <div class='detail-value'>{fullTableInfo}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Dátum</div>
                    <div class='detail-value'>{date}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Időpont</div>
                    <div class='detail-value'>{time}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Vendégek</div>
                    <div class='detail-value'>{guests} fő</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Helyszín</div>
                    <div class='detail-value'>{location}</div>
                </div>
            </div>
            
            {(model.HtmlServices != null && model.HtmlServices.Any() ? $@"
            <h3 style='color: #4a5568; margin-top: 20px;'>Kiválasztott szolgáltatások</h3>
            {servicesHtml}
            " : "")}
            
            {(!string.IsNullOrEmpty(notes) && notes != "- Nincs megjegyzés -" ? $@"
            <div class='important-note'>
                <h4 style='margin-top: 0; color: #92400e;'>Megjegyzés</h4>
                <p>{notes}</p>
            </div>
            " : "")}
            
            <div class='important-note'>
                <h4 style='margin-top: 0; color: #92400e;'>Fontos információ</h4>
                <ul style='margin: 10px 0 10px 20px;'>
                    <li>Kérjük, érkezz pontosan a foglalt időpontra</li>
                    <li>Asztalod 15 perccel a foglalt időpont után továbbadásra kerül</li>
                    <li>Ha változtatnod kell, kérjük jelezd minél hamarabb</li>
                    <li>A foglalás módosítása vagy lemondása a profilodban lehetséges</li>
                </ul>
            </div>
            
            <p style='margin-top: 25px;'>
                Szívesen látunk éttermünkben!<br>
                Várunk szeretettel!
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #10b981;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Cím: 1234 Budapest, Példa utca 1.</p>
            <p>Telefon: +36 1 234 5678 | E-mail: aethrarestaurant@gmail.com</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building reservation approval email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family: Arial, sans-serif;'>
    <div style='background: #10b981; color: white; padding: 20px; text-align: center;'>
        <h1>✅ Foglalásodat elfogadtuk!</h1>
    </div>
    <div style='padding: 20px;'>
        <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
        <p>Asztalfoglalásodat sikeresen elfogadtuk.</p>
        <p><strong>Foglalási azonosító:</strong> #{model.ReservationId ?? "N/A"}</p>
        <p><strong>Asztal:</strong> {model.TableName ?? "Asztal"} {(string.IsNullOrEmpty(model.TableNumber) ? "" : $"({model.TableNumber})")}</p>
        <p><strong>Időpont:</strong> {model.Date ?? ""} {model.Time ?? ""}</p>
        <p><strong>Vendégek:</strong> {model.Guests?.ToString() ?? "1"} fő</p>
        <p>Kérjük, érkezz pontosan a foglalt időpontra.</p>
        <p style='margin-top: 30px;'>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
    </div>
</body>
</html>";
        }
    }
    
    // RENDELÉS ELFOGADÁSI EMAIL
    private string BuildOrderApprovalEmail(OrderApprovalModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Vendég";
            var orderId = model.OrderId ?? "N/A";
            var totalAmount = model.TotalAmount.ToString("N0");
            var estimatedDelivery = model.EstimatedDelivery ?? "30-45 perc";
            var orderDate = model.OrderDate ?? DateTime.Now.ToString("yyyy.MM.dd. HH:mm");
            
            // Tételek táblázat
            string itemsTable = "";
            if (model.Items != null && model.Items.Count > 0)
            {
                foreach (var item in model.Items)
                {
                    var itemName = item?.Name ?? "Termék";
                    var quantity = item?.Quantity.ToString() ?? "1";
                    var price = (item?.Price ?? 0).ToString("N0");
                    var total = ((item?.Price ?? 0) * (item?.Quantity ?? 1)).ToString("N0");
                    
                    itemsTable += $@"
                    <tr>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd;'>{itemName}</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: center;'>{quantity}</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: right;'>{price} Ft</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: right;'>{total} Ft</td>
                    </tr>";
                }
            }
            else
            {
                itemsTable = @"<tr><td colspan='4' style='padding: 10px; text-align: center;'>Nincsenek tételek</td></tr>";
            }
            
            // Foglalás információ
            string reservationInfo = "";
            if (!string.IsNullOrEmpty(model.ReservationId))
            {
                reservationInfo = $@"
                <div style='background-color: #f0f8ff; border: 1px solid #87ceeb; border-radius: 5px; padding: 15px; margin: 20px 0;'>
                    <h4 style='color: #1e90ff; margin-top: 0;'>Asztalfoglalás</h4>
                    <p>Rendelésedhez tartozik egy asztalfoglalás (azonosító: {model.ReservationId}).</p>
                </div>";
            }
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Rendelés feldolgozás</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #eff6ff;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #3b82f6;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }}
        th {{
            background-color: #3b82f6;
            color: white;
            padding: 12px;
            text-align: left;
            font-weight: 600;
        }}
        td {{
            padding: 12px;
            border-bottom: 1px solid #e2e8f0;
        }}
        .total {{
            font-weight: bold;
            background-color: #f8fafc;
        }}
        .status-badge {{
            background: #3b82f6;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .progress-container {{
            background: #f1f5f9;
            border-radius: 10px;
            padding: 20px;
            margin: 20px 0;
        }}
        .progress-step {{
            display: flex;
            align-items: center;
            margin-bottom: 15px;
        }}
        .step-number {{
            background: #3b82f6;
            color: white;
            width: 30px;
            height: 30px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            font-weight: bold;
            margin-right: 15px;
        }}
        .step-active {{
            background: #10b981;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Rendelésed feldolgozás alatt!</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <p><span class='status-badge'>FELDOLGOZÁS ALATT</span></p>
                <p><strong>Rendelési azonosító:</strong> #{orderId}</p>
                <p><strong>Rendelés dátuma:</strong> {orderDate}</p>
                <p><strong>Várható kiszállítás:</strong> {estimatedDelivery}</p>
            </div>
            
            <p>Rendelésed elfogadtuk és most konyhánk elkészíti étkeid! Az alábbiakban találod a rendelés részleteit:</p>
            
            <h3 style='color: #4a5568; margin-top: 20px;'>Rendelt tételek</h3>
            <table>
                <thead>
                    <tr>
                        <th>Termék</th>
                        <th style='text-align: center;'>Darab</th>
                        <th style='text-align: right;'>Egységár</th>
                        <th style='text-align: right;'>Összesen</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsTable}
                </tbody>
                <tfoot>
                    <tr class='total'>
                        <td colspan='3' style='text-align: right; font-size: 1.1em;'>Összesen:</td>
                        <td style='text-align: right; font-size: 1.1em; color: #3b82f6; font-weight: bold;'>{totalAmount} Ft</td>
                    </tr>
                </tfoot>
            </table>
            
            {reservationInfo}
            
            <div class='progress-container'>
                <h4 style='margin-top: 0; color: #1e293b;'>Rendelésed állapota</h4>
                
                <div class='progress-step'>
                    <div class='step-number step-active'>1</div>
                    <div>
                        <strong>Rendelés elfogadva</strong><br>
                        <small>{DateTime.Now:HH:mm}</small>
                    </div>
                </div>
                
                <div class='progress-step'>
                    <div class='step-number'>2</div>
                    <div>
                        <strong>Ételek elkészítése</strong><br>
                        <small>Folyamatban</small>
                    </div>
                </div>
                
                <div class='progress-step'>
                    <div class='step-number'>3</div>
                    <div>
                        <strong>Kiszállítva</strong><br>
                        <small>Hamarosan</small>
                    </div>
                </div>
            </div>
            
            <p style='margin-top: 25px;'>
                Amint rendelésed elkészült, értesítünk.<br>
                Köszönjük a rendelést!
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #3b82f6;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building order approval email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body>
    <h1>✅ Rendelésed feldolgozás alatt!</h1>
    <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
    <p>Rendelésed elfogadtuk és feldolgozás alatt van.</p>
    <p><strong>Rendelési azonosító:</strong> #{model.OrderId ?? "N/A"}</p>
    <p><strong>Összeg:</strong> {model.TotalAmount.ToString("N0")} Ft</p>
    <p>Ételeid hamarosan elkészülnek.</p>
    <p>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
</body>
</html>";
        }
    }
    
    // RENDELÉS KISZÁLLÍTÁSI EMAIL
    private string BuildOrderDeliveredEmail(OrderDeliveredModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Vendég";
            var orderId = model.OrderId ?? "N/A";
            var deliveryTime = model.DeliveryTime ?? DateTime.Now.ToString("HH:mm");
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Rendelés kiszállítva</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #10b981 0%, #059669 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #f0fdf4;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #10b981;
            text-align: center;
        }}
        .status-badge {{
            background: #10b981;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .icon-large {{
            font-size: 48px;
            color: #10b981;
            margin: 20px 0;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🚚 Rendelésed kiszállítva!</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <div class='icon-large'>✅</div>
                <p><span class='status-badge'>KISZÁLLÍTVA</span></p>
                <p><strong>Rendelési azonosító:</strong> #{orderId}</p>
                <p><strong>Kiszállítás ideje:</strong> {deliveryTime}</p>
            </div>
            
            <p style='text-align: center; font-size: 1.1em;'>
                Rendelésed sikeresen kiszállítottuk!<br>
                Reméljük, ízlenek az ételeid!
            </p>
            
            <div style='background: #f8fafc; border-radius: 8px; padding: 20px; margin: 25px 0; text-align: center;'>
                <h4 style='color: #1e293b; margin-top: 0;'>Visszajelzésed fontos számunkra!</h4>
                <p>Ossza meg velünk, hogy tetszett a rendelése:</p>
                <a href='https://etterem.hu/ertekeles' style='
                    display: inline-block;
                    background: linear-gradient(135deg, #3b82f6, #1d4ed8);
                    color: white;
                    padding: 12px 24px;
                    border-radius: 6px;
                    text-decoration: none;
                    font-weight: bold;
                    margin-top: 10px;
                '>Értékelés küldése</a>
            </div>
            
            <p style='margin-top: 25px; text-align: center;'>
                Köszönjük, hogy minket választottál!<br>
                Várunk vissza szeretettel!
            </p>
            
            <p style='margin-top: 30px; text-align: center;'>
                Üdvözlettel,<br>
                <strong style='color: #10b981;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ha kérdésed van, keress minket a aethrarestaurant@gmail.com címen.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building order delivered email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body>
    <h1>🚚 Rendelésed kiszállítva!</h1>
    <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
    <p>Rendelésed sikeresen kiszállítottuk.</p>
    <p><strong>Rendelési azonosító:</strong> #{model.OrderId ?? "N/A"}</p>
    <p><strong>Kiszállítás ideje:</strong> {model.DeliveryTime ?? DateTime.Now.ToString("HH:mm")}</p>
    <p>Reméljük, ízlenek az ételeid!</p>
    <p>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
</body>
</html>";
        }
    }
    
    // FOGLALÁS ELUTASÍTÁSI EMAIL
    private string BuildReservationRejectionEmail(ReservationRejectionModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Vendég";
            var reservationId = model.ReservationId ?? "N/A";
            var tableName = model.TableName ?? "Asztal";
            var date = model.Date ?? DateTime.Now.ToString("yyyy.MM.dd.");
            var time = model.Time ?? "-";
            var guests = model.Guests?.ToString() ?? "1";
            var rejectionReason = model.RejectionReason ?? "Sajnos az asztal foglalás nem teljesíthető a kért időpontban.";
            var rejectionDate = model.RejectionDate ?? DateTime.Now.ToString("yyyy.MM.dd.");
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Foglalás elutasítva</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #fef2f2;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #ef4444;
        }}
        .status-badge {{
            background: #ef4444;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .detail-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 15px;
            margin: 20px 0;
        }}
        .detail-item {{
            background: #f8fafc;
            padding: 15px;
            border-radius: 6px;
            border: 1px solid #e2e8f0;
        }}
        .detail-label {{
            font-weight: 600;
            color: #475569;
            font-size: 0.9rem;
            margin-bottom: 5px;
        }}
        .detail-value {{
            font-size: 1.1rem;
            font-weight: 700;
            color: #1e293b;
        }}
        .rejection-reason {{
            background: #fef3c7;
            border: 1px solid #f59e0b;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
        .alternative {{
            background: #f0fdf4;
            border: 1px solid #10b981;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>❌ Foglalásodat elutasítottuk</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <p><span class='status-badge'>ELUTASÍTVA</span></p>
                <p><strong>Foglalási azonosító:</strong> #{reservationId}</p>
                <p><strong>Elutasítás dátuma:</strong> {rejectionDate}</p>
            </div>
            
            <p>Sajnálattal értesítjük, hogy asztalfoglalásodat nem tudtuk elfogadni.</p>
            
            <div class='detail-grid'>
                <div class='detail-item'>
                    <div class='detail-label'>Asztal</div>
                    <div class='detail-value'>{tableName}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Dátum</div>
                    <div class='detail-value'>{date}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Időpont</div>
                    <div class='detail-value'>{time}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Vendégek</div>
                    <div class='detail-value'>{guests} fő</div>
                </div>
            </div>
            
            <div class='rejection-reason'>
                <h4 style='margin-top: 0; color: #92400e;'>Elutasítás oka</h4>
                <p>{rejectionReason}</p>
            </div>
            
            <div class='alternative'>
                <h4 style='margin-top: 0; color: #166534;'>Alternatívák</h4>
                <p>Javasoljuk, hogy próbálj újra foglalni:</p>
                <ul style='margin: 10px 0 10px 20px;'>
                    <li>Válassz másik időpontot</li>
                    <li>Próbálj foglalni másik asztalt</li>
                    <li>Kisebb csoporttal próbálkozz</li>
                </ul>
                <a href='https://etterem.hu/foglalas' style='
                    display: inline-block;
                    background: linear-gradient(135deg, #10b981, #059669);
                    color: white;
                    padding: 10px 20px;
                    border-radius: 6px;
                    text-decoration: none;
                    font-weight: bold;
                    margin-top: 10px;
                '>Új foglalás</a>
            </div>
            
            <p style='margin-top: 25px;'>
                Bármilyen kérdésed van, keress minket bizalommal!<br>
                Várunk vissza szeretettel!
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #ef4444;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building reservation rejection email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body>
    <h1>❌ Foglalásodat elutasítottuk</h1>
    <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
    <p>Sajnálattal értesítjük, hogy asztalfoglalásodat nem tudtuk elfogadni.</p>
    <p><strong>Foglalási azonosító:</strong> #{model.ReservationId ?? "N/A"}</p>
    <p><strong>Oka:</strong> {model.RejectionReason ?? "Sajnos az asztal foglalás nem teljesíthető."}</p>
    <p>Kérjük, próbálj újra foglalni másik időpontban.</p>
    <p>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
</body>
</html>";
        }
    }
    
    // RENDELÉS ELUTASÍTÁSI EMAIL
    private string BuildOrderRejectionEmail(OrderRejectionModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Vendég";
            var orderId = model.OrderId ?? "N/A";
            var rejectionReason = model.RejectionReason ?? "A rendelést sajnos nem tudtuk teljesíteni.";
            var totalAmount = model.TotalAmount.ToString("N0");
            var rejectionDate = model.RejectionDate ?? DateTime.Now.ToString("yyyy.MM.dd.");
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Rendelés elutasítva</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #fef2f2;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #ef4444;
        }}
        .status-badge {{
            background: #ef4444;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .rejection-reason {{
            background: #fef3c7;
            border: 1px solid #f59e0b;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
        .alternative {{
            background: #f0fdf4;
            border: 1px solid #10b981;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>❌ Rendelésedet elutasítottuk</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <p><span class='status-badge'>ELUTASÍTVA</span></p>
                <p><strong>Rendelési azonosító:</strong> #{orderId}</p>
                <p><strong>Rendelés dátuma:</strong> {model.OrderDate ?? "N/A"}</p>
                <p><strong>Elutasítás dátuma:</strong> {rejectionDate}</p>
                <p><strong>Rendelés összege:</strong> {totalAmount} Ft</p>
            </div>
            
            <p>Sajnálattal értesítjük, hogy rendelésedet nem tudtuk teljesíteni.</p>
            
            <div class='rejection-reason'>
                <h4 style='margin-top: 0; color: #92400e;'>Elutasítás oka</h4>
                <p>{rejectionReason}</p>
            </div>
            
            <div class='alternative'>
                <h4 style='margin-top: 0; color: #166534;'>Mit tehetsz most?</h4>
                <ul style='margin: 10px 0 10px 20px;'>
                    <li>Rendelj másik ételeket a menüből</li>
                    <li>Próbálkozz később újra</li>
                    <li>Válassz másik szállítási módot</li>
                </ul>
                <a href='https://etterem.hu/menu' style='
                    display: inline-block;
                    background: linear-gradient(135deg, #10b981, #059669);
                    color: white;
                    padding: 12px 24px;
                    border-radius: 6px;
                    text-decoration: none;
                    font-weight: bold;
                    margin-top: 10px;
                '>Új rendelés</a>
            </div>
            
            <p style='margin-top: 25px;'>
                Bármilyen kérdésed van, keress minket bizalommal!<br>
                Várunk vissza szeretettel!
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #ef4444;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building order rejection email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body>
    <h1>❌ Rendelésedet elutasítottuk</h1>
    <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
    <p>Sajnálattal értesítjük, hogy rendelésedet nem tudtuk teljesíteni.</p>
    <p><strong>Rendelési azonosító:</strong> #{model.OrderId ?? "N/A"}</p>
    <p><strong>Oka:</strong> {model.RejectionReason ?? "A rendelést sajnos nem tudtuk teljesíteni."}</p>
    <p>Kérjük, próbálj újra rendelni.</p>
    <p>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
</body>
</html>";
        }
    }
    
    // RENDELÉS MEGERŐSÍTÉS EMAIL
    private string BuildOrderConfirmationEmail(OrderConfirmationModel model)
    {
        try
        {
            // Alap adatok
            var userName = model.UserName ?? "Kedves Vendég";
            var orderId = model.OrderId ?? "N/A";
            var totalAmount = model.TotalAmount.ToString("N0");
            var serviceFee = model.ServiceFee.ToString("N0");
            var itemsTotal = (model.TotalAmount - model.ServiceFee).ToString("N0");
            var orderDate = model.OrderDate ?? DateTime.Now.ToString("yyyy.MM.dd. HH:mm");
            
            // Tételek táblázat
            string itemsTable = "";
            if (model.Items != null && model.Items.Count > 0)
            {
                foreach (var item in model.Items)
                {
                    var itemName = item?.Name ?? "Termék";
                    var quantity = item?.Quantity.ToString() ?? "1";
                    var price = (item?.Price ?? 0).ToString("N0");
                    var total = ((item?.Price ?? 0) * (item?.Quantity ?? 1)).ToString("N0");
                    
                    itemsTable += $@"
                    <tr>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd;'>{itemName}</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: center;'>{quantity}</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: right;'>{price} Ft</td>
                        <td style='padding: 10px; border-bottom: 1px solid #ddd; text-align: right;'>{total} Ft</td>
                    </tr>";
                }
            }
            else
            {
                itemsTable = @"<tr><td colspan='4' style='padding: 10px; text-align: center;'>Nincsenek tételek</td></tr>";
            }
            
            // Asztalfoglalás részletek
            string reservationSection = "";
            if (model.Reservation != null && !string.IsNullOrEmpty(model.Reservation.ReservationId))
            {
                var tableName = model.Reservation.TableName ?? "Asztal";
                var tableNumber = model.Reservation.TableNumber ?? "";
                var date = model.Reservation.Date ?? "";
                var time = model.Reservation.Time ?? "";
                var guests = model.Reservation.Guests?.ToString() ?? "1";
                var location = model.Reservation.TableLocation ?? "";
                var reservationMessage = model.Reservation.Message ?? "";
                
                reservationSection = $@"
                <div style='background-color: #f0f8ff; border: 1px solid #87ceeb; border-radius: 5px; padding: 15px; margin: 20px 0;'>
                    <h3 style='color: #1e90ff; margin-top: 0;'>Asztalfoglalás részletei</h3>
                    <div style='display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 10px;'>
                        <div>
                            <strong>Asztal:</strong><br>
                            {tableName} {(!string.IsNullOrEmpty(tableNumber) ? $"(szám: {tableNumber})" : "")}
                        </div>
                        <div>
                            <strong>Dátum:</strong><br>
                            {(!string.IsNullOrEmpty(date) ? date : "Nincs megadva")}
                        </div>
                        <div>
                            <strong>Időpont:</strong><br>
                            {(!string.IsNullOrEmpty(time) ? time : "Nincs megadva")}
                        </div>
                        <div>
                            <strong>Vendégek:</strong><br>
                            {guests} fő
                        </div>
                        <div>
                            <strong>Helyszín:</strong><br>
                            {(!string.IsNullOrEmpty(location) ? location : "Nincs megadva")}
                        </div>";
                
                if (!string.IsNullOrEmpty(reservationMessage))
                {
                    reservationSection += $@"
                        <div style='grid-column: 1 / -1;'>
                            <strong>Megjegyzés:</strong><br>
                            {reservationMessage}
                        </div>";
                }
                
                reservationSection += @"
                    </div>
                </div>";
            }
            else if (!string.IsNullOrEmpty(model.ReservationId))
            {
                reservationSection = $@"
                <div style='background-color: #f0f8ff; border: 1px solid #87ceeb; border-radius: 5px; padding: 15px; margin: 20px 0;'>
                    <h3 style='color: #1e90ff; margin-top: 0;'>Asztalfoglalás</h3>
                    <p>Rendelésedhez asztalfoglalás tartozik (azonosító: {model.ReservationId}).</p>
                </div>";
            }
            
            // Megjegyzés
            string notesSection = "";
            if (!string.IsNullOrEmpty(model.Notes))
            {
                notesSection = $@"
                <div style='background-color: #fffacd; border: 1px solid #ffd700; border-radius: 5px; padding: 15px; margin: 15px 0;'>
                    <h4 style='margin-top: 0; color: #8b6914;'>Megjegyzés</h4>
                    <p style='font-style: italic;'>{model.Notes}</p>
                </div>";
            }
            
            // Fogyasztási mód
            var consumptionText = model.ConsumptionMode switch
            {
                "restaurant" => "Étteremben",
                "takeaway" => "Elvitelre",
                "delivery" => "Házhozszállítás",
                _ => "Étteremben"
            };
            
            // HTML sablon összeállítása
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Rendelés megerősítés</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .order-info {{
            background: #f8fafc;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #667eea;
        }}
        table {{
            width: 100%;
            border-collapse: collapse;
            margin: 20px 0;
        }}
        th {{
            background-color: #667eea;
            color: white;
            padding: 12px;
            text-align: left;
            font-weight: 600;
        }}
        td {{
            padding: 12px;
            border-bottom: 1px solid #e2e8f0;
        }}
        .total {{
            font-weight: bold;
            background-color: #f8fafc;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
        .status-badge {{
            background: #10b981;
            color: white;
            padding: 6px 12px;
            border-radius: 20px;
            font-size: 12px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 10px;
        }}
        .highlight {{
            color: #667eea;
            font-weight: bold;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>Rendelés megerősítés</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <span class='highlight'>{userName}</span>!</p>
            
            <div class='order-info'>
                <p><span class='status-badge'>Feldolgozás alatt</span></p>
                <p><strong>Rendelési azonosító:</strong> #{orderId}</p>
                <p><strong>Rendelés dátuma:</strong> {orderDate}</p>
                <p><strong>Fizetendő összeg:</strong> {totalAmount} Ft</p>
            </div>
            
            <p>Köszönjük, hogy nálunk rendeltél! Rendelésed hamarosan feldolgozásra kerül.</p>
            
            {reservationSection}
            
            <h3 style='color: #4a5568; margin-top: 30px;'>Rendelt tételek</h3>
            <table>
                <thead>
                    <tr>
                        <th>Termék</th>
                        <th style='text-align: center;'>Darab</th>
                        <th style='text-align: right;'>Egységár</th>
                        <th style='text-align: right;'>Összesen</th>
                    </tr>
                </thead>
                <tbody>
                    {itemsTable}
                </tbody>
                <tfoot>
                    <tr class='total'>
                        <td colspan='3' style='text-align: right;'>Ételek összege:</td>
                        <td style='text-align: right;'>{itemsTotal} Ft</td>
                    </tr>
                    <tr class='total'>
                        <td colspan='3' style='text-align: right;'>Szolgáltatási díj:</td>
                        <td style='text-align: right;'>{serviceFee} Ft</td>
                    </tr>
                    <tr class='total'>
                        <td colspan='3' style='text-align: right; font-size: 1.1em;'>Összesen:</td>
                        <td style='text-align: right; font-size: 1.1em; color: #667eea; font-weight: bold;'>{totalAmount} Ft</td>
                    </tr>
                </tfoot>
            </table>
            
            <p><strong>Fogyasztási mód:</strong> {consumptionText}</p>
            
            {notesSection}
            
            <p style='margin-top: 25px;'>
                A rendelésed aktuális státuszáról emailben értesítünk.<br>
                Kérjük, tartsd meg ezt az emailt a rendelésed nyomon követéséhez.
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #667eea;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
            <p>Ha kérdésed van, keress minket a aethrarestaurant@gmail.com címen.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building order confirmation email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body>
    <h1>Rendelés megerősítés</h1>
    <p>Tisztelt {model.UserName ?? "Vendég"}!</p>
    <p>Rendelésed sikeresen rögzítettük.</p>
    <p><strong>Rendelési azonosító:</strong> #{model.OrderId ?? "N/A"}</p>
    <p><strong>Összeg:</strong> {model.TotalAmount.ToString("N0")} Ft</p>
    <p><strong>Dátum:</strong> {DateTime.Now:yyyy.MM.dd. HH:mm}</p>
    <p>Köszönjük a rendelést!</p>
    <p>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
</body>
</html>";
        }
    }
    
    // REGISZTRÁCIÓ VERIFIKÁCIÓ EMAIL
    [HttpPost("SendVerificationEmail")]
    public async Task<IActionResult> SendVerificationEmail()
    {
        try
        {
            Console.WriteLine($"📧 Regisztráció verifikációs email küldése: {DateTime.Now}");
            
            string rawBody;
            using (var reader = new StreamReader(Request.Body))
            {
                rawBody = await reader.ReadToEndAsync();
            }
            
            if (string.IsNullOrEmpty(rawBody))
            {
                return BadRequest(new { success = false, message = "Empty request body" });
            }
            
            VerificationEmailModel? model;
            try
            {
                model = JsonConvert.DeserializeObject<VerificationEmailModel>(rawBody);
                
                if (model == null)
                {
                    return BadRequest(new { success = false, message = "Failed to deserialize model" });
                }
            }
            catch (JsonException jsonEx)
            {
                return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
            }
            
            // Validációk
            model.UserName = model.UserName ?? "Kedves Felhasználó";
            model.Email = model.Email ?? "no-email@example.com";
            model.VerificationCode = model.VerificationCode ?? "123456";
            
            // Email üzenet létrehozása (KÉK TÉMÁVAL)
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("AETHRA", _smtpUsername));
            message.To.Add(new MailboxAddress(model.UserName, model.Email));
            message.Subject = $"✅ Regisztráció megerősítése - AETHRA";
            
            // Email body generálása (kék témával)
            var emailBody = BuildVerificationEmail(model);
            
            message.Body = new TextPart("html")
            {
                Text = emailBody
            };
            
            // SMTP küldés
            var emailSent = await SendEmailAsync(message);
            
            if (emailSent)
            {
                return Ok(new { 
                    success = true, 
                    message = "Verification email sent successfully!",
                    emailSentTo = model.Email
                });
            }
            else
            {
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to send verification email"
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Verifikációs email küldési hiba: {ex.Message}");
            return StatusCode(500, new { 
                success = false, 
                message = $"Verification email sending failed. Error: {ex.Message}"
            });
        }
    }
    
    // REGISZTRÁCIÓ VERIFIKÁCIÓ EMAIL TEMPLATE
    private string BuildVerificationEmail(VerificationEmailModel model)
    {
        try
        {
            var userName = model.UserName ?? "Kedves Felhasználó";
            var verificationCode = model.VerificationCode ?? "123456";
            
            var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Regisztráció megerősítése</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #3b82f6 0%, #1d4ed8 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #eff6ff;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #3b82f6;
            text-align: center;
        }}
        .verification-code {{
            background: #f8fafc;
            border: 2px dashed #3b82f6;
            border-radius: 8px;
            padding: 25px;
            margin: 25px 0;
            text-align: center;
            font-family: monospace;
            font-size: 32px;
            font-weight: bold;
            color: #1e293b;
            letter-spacing: 5px;
        }}
        .status-badge {{
            background: #3b82f6;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
        .important-note {{
            background: #fef3c7;
            border: 1px solid #f59e0b;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Regisztráció megerősítése</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>
            
            <div class='info-box'>
                <p><span class='status-badge'>REGISZTRÁCIÓ</span></p>
                <p>Köszönjük, hogy regisztráltál az <strong>AETHRA</strong> oldalán!</p>
                <p>A regisztráció befejezéséhez kérjük, add meg az alábbi ellenőrző kódot:</p>
            </div>
            
            <div class='verification-code'>
                {verificationCode}
            </div>
            
            <div class='important-note'>
                <h4 style='margin-top: 0; color: #92400e;'>Fontos információ</h4>
                <ul style='margin: 10px 0 10px 20px;'>
                    <li>Az ellenőrző kód <strong>15 percig</strong> érvényes</li>
                    <li>Ne add meg ezt a kódot senkinek!</li>
                    <li>Ha nem te kezdeményezted ezt a regisztrációt, hagyd figyelmen kívül ezt az emailt</li>
                </ul>
            </div>
            
            <p style='text-align: center; margin-top: 25px;'>
                A kód megadása után teljes hozzáférést kapsz az oldal összes funkciójához!
            </p>
            
            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #3b82f6;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
            <p>Ha kérdésed van, keress minket a aethrarestaurant@gmail.com címen.</p>
        </div>
    </div>
</body>
</html>";

            return emailHtml;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error building verification email: {ex.Message}");
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family: Arial, sans-serif;'>
    <div style='background: #3b82f6; color: white; padding: 20px; text-align: center;'>
        <h1>✅ Regisztráció megerősítése</h1>
    </div>
    <div style='padding: 20px;'>
        <p>Tisztelt {model.UserName ?? "Kedves Felhasználó"}!</p>
        <p>Köszönjük, hogy regisztráltál az AETHRA oldalán!</p>
        <p style='font-size: 24px; font-weight: bold; text-align: center; margin: 30px 0; color: #1d4ed8;'>
            {model.VerificationCode ?? "123456"}
        </p>
        <p>Add meg ezt a kódot a regisztráció befejezéséhez.</p>
        <p>A kód 15 percig érvényes.</p>
        <p style='margin-top: 30px;'>
            Üdvözlettel,<br>
            <strong style='color: #3b82f6;'>AETHRA Étterem Csapata</strong>
        </p>
    </div>
</body>
</html>";
        }
    }
    [HttpPost("SendReservationReminder")]
public async Task<IActionResult> SendReservationReminder()
{
    try
    {
        Console.WriteLine($"📧 Foglalási emlékeztető email küldése: {DateTime.Now}");

        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrEmpty(rawBody))
        {
            return BadRequest(new { success = false, message = "Empty request body" });
        }

        ReservationReminderModel? model;
        try
        {
            model = JsonConvert.DeserializeObject<ReservationReminderModel>(rawBody);

            if (model == null)
            {
                return BadRequest(new { success = false, message = "Failed to deserialize model" });
            }
        }
        catch (JsonException jsonEx)
        {
            return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
        }

        // Validációk
        model.UserName = model.UserName ?? "Kedves Vendég";
        model.Email = model.Email ?? "no-email@example.com";
        model.ReservationId = model.ReservationId ?? "N/A";
        model.TableName = model.TableName ?? "Asztal";
        model.Date = model.Date ?? DateTime.Now.AddDays(1).ToString("yyyy.MM.dd.");
        model.Time = model.Time ?? "-";
        model.Guests = model.Guests ?? 1;
        model.TableLocation = model.TableLocation ?? "Éttermünkben";

        // Email üzenet létrehozása
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("AETHRA Étterem", _smtpUsername));
        message.To.Add(new MailboxAddress(model.UserName, model.Email));
        message.Subject = $"🔔 Emlékeztető: Holnapi asztalfoglalásod - #{model.ReservationId}";

        // Email body generálása
        var emailBody = BuildReservationReminderEmail(model);

        message.Body = new TextPart("html")
        {
            Text = emailBody
        };

        // SMTP küldés
        var emailSent = await SendEmailAsync(message);

        if (emailSent)
        {
            return Ok(new
            {
                success = true,
                message = "Reservation reminder email sent successfully!",
                emailSentTo = model.Email
            });
        }
        else
        {
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to send reservation reminder email"
            });
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Foglalási emlékeztető email küldési hiba: {ex.Message}");
        return StatusCode(500, new
        {
            success = false,
            message = $"Reservation reminder email sending failed. Error: {ex.Message}"
        });
    }
}

// FOGLALÁSI EMLÉKEZTETŐ EMAIL TEMPLATE
private string BuildReservationReminderEmail(ReservationReminderModel model)
{
    try
    {
        var userName = model.UserName ?? "Kedves Vendég";
        var reservationId = model.ReservationId ?? "N/A";
        var tableName = model.TableName ?? "Asztal";
        var tableNumber = model.TableNumber ?? "";
        var date = model.Date ?? DateTime.Now.AddDays(1).ToString("yyyy.MM.dd.");
        var time = model.Time ?? "-";
        var guests = model.Guests?.ToString() ?? "1";
        var location = model.TableLocation ?? "Éttermünkben";
        var currentDate = DateTime.Now.ToString("yyyy.MM.dd. HH:mm");

        var fullTableInfo = !string.IsNullOrEmpty(tableNumber)
            ? $"{tableName} (szám: {tableNumber})"
            : tableName;

        var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Foglalási emlékeztető</title>
    <style>
        body {{
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            line-height: 1.6;
            color: #333;
            max-width: 600px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .container {{
            background: white;
            border-radius: 10px;
            overflow: hidden;
            box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        }}
        .header {{
            background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .header h1 {{
            margin: 0;
            font-size: 24px;
        }}
        .content {{
            padding: 30px;
        }}
        .info-box {{
            background: #fffbeb;
            padding: 20px;
            border-radius: 8px;
            margin-bottom: 25px;
            border-left: 4px solid #f59e0b;
        }}
        .detail-grid {{
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
            gap: 15px;
            margin: 20px 0;
        }}
        .detail-item {{
            background: #f8fafc;
            padding: 15px;
            border-radius: 6px;
            border: 1px solid #e2e8f0;
        }}
        .detail-label {{
            font-weight: 600;
            color: #475569;
            font-size: 0.9rem;
            margin-bottom: 5px;
        }}
        .detail-value {{
            font-size: 1.1rem;
            font-weight: 700;
            color: #1e293b;
        }}
        .status-badge {{
            background: #f59e0b;
            color: white;
            padding: 8px 16px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
            display: inline-block;
            margin-bottom: 15px;
        }}
        .footer {{
            text-align: center;
            margin-top: 30px;
            color: #666;
            font-size: 12px;
            border-top: 1px solid #e2e8f0;
            padding-top: 20px;
        }}
        .important-note {{
            background: #fef3c7;
            border: 1px solid #f59e0b;
            border-radius: 6px;
            padding: 15px;
            margin: 20px 0;
        }}
        .button {{
            display: inline-block;
            background: linear-gradient(135deg, #f59e0b, #d97706);
            color: white;
            padding: 12px 24px;
            border-radius: 6px;
            text-decoration: none;
            font-weight: bold;
            margin-top: 20px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔔 Emlékeztető: Holnapi asztalfoglalásod</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{userName}</strong>!</p>

            <div class='info-box'>
                <p><span class='status-badge'>HOLNAP ESEDÉKES</span></p>
                <p><strong>Foglalási azonosító:</strong> #{reservationId}</p>
                <p><strong>Emlékeztető dátuma:</strong> {currentDate}</p>
            </div>

            <p>Ezúton szeretnénk emlékeztetni, hogy <strong>holnap</strong> asztalfoglalásod van nálunk! Az alábbiakban találod a foglalás részleteit:</p>

            <div class='detail-grid'>
                <div class='detail-item'>
                    <div class='detail-label'>Asztal</div>
                    <div class='detail-value'>{fullTableInfo}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Dátum</div>
                    <div class='detail-value'>{date}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Időpont</div>
                    <div class='detail-value'>{time}</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Vendégek</div>
                    <div class='detail-value'>{guests} fő</div>
                </div>
                <div class='detail-item'>
                    <div class='detail-label'>Helyszín</div>
                    <div class='detail-value'>{location}</div>
                </div>
            </div>

            <div class='important-note'>
                <h4 style='margin-top: 0; color: #92400e;'>Fontos információk</h4>
                <ul style='margin: 10px 0 10px 20px;'>
                    <li>Kérjük, érkezz pontosan a foglalt időpontra</li>
                    <li>Asztalod 15 perccel a foglalt időpont után továbbadásra kerül</li>
                    <li>Ha mégsem tudsz eljönni, kérjük jelezd minél hamarabb</li>
                    <li>A foglalás módosítása vagy lemondása a profilodban lehetséges</li>
                </ul>
            </div>

            <div style='text-align: center;'>
                <a href='https://etterem.hu/profile' class='button'>Foglalásaim kezelése</a>
            </div>

            <p style='margin-top: 25px;'>
                Szívesen látunk holnap éttermünkben!<br>
                Várunk szeretettel!
            </p>

            <p style='margin-top: 30px;'>
                Üdvözlettel,<br>
                <strong style='color: #f59e0b;'>AETHRA Étterem Csapata</strong>
            </p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
            <p>Cím: 1234 Budapest, Példa utca 1.</p>
            <p>Telefon: +36 1 234 5678 | E-mail: aethrarestaurant@gmail.com</p>
            <p>Ez egy automatikus üzenet, kérjük ne válaszoljon rá.</p>
        </div>
    </div>
</body>
</html>";

        return emailHtml;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Error building reservation reminder email: {ex.Message}");
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
</head>
<body style='font-family: Arial, sans-serif;'>
    <div style='background: #f59e0b; color: white; padding: 20px; text-align: center;'>
        <h1>🔔 Emlékeztető: Holnapi asztalfoglalásod</h1>
    </div>
    <div style='padding: 20px;'>
        <p>Tisztelt {model.UserName ?? "Kedves Vendég"}!</p>
        <p>Ezúton szeretnénk emlékeztetni, hogy <strong>holnap</strong> asztalfoglalásod van nálunk!</p>
        <p><strong>Foglalási azonosító:</strong> #{model.ReservationId ?? "N/A"}</p>
        <p><strong>Asztal:</strong> {model.TableName ?? "Asztal"} {(string.IsNullOrEmpty(model.TableNumber) ? "" : $"({model.TableNumber})")}</p>
        <p><strong>Időpont:</strong> {model.Date ?? ""} {model.Time ?? ""}</p>
        <p><strong>Vendégek:</strong> {model.Guests?.ToString() ?? "1"} fő</p>
        <p>Kérjük, érkezz pontosan a foglalt időpontra.</p>
        <p style='margin-top: 30px;'>Üdvözlettel,<br>AETHRA Étterem Csapata</p>
    </div>
</body>
</html>";
    }
}
// Jelszó módosítás verifikációs email
[HttpPost("SendPasswordChangeVerification")]
public async Task<IActionResult> SendPasswordChangeVerification()
{
    try
    {
        Console.WriteLine($"📧 Jelszó módosítás verifikációs email küldése: {DateTime.Now}");
        
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        
        if (string.IsNullOrEmpty(rawBody))
        {
            return BadRequest(new { success = false, message = "Empty request body" });
        }
        
        PasswordChangeVerificationModel? model;
        try
        {
            model = JsonConvert.DeserializeObject<PasswordChangeVerificationModel>(rawBody);
            if (model == null)
            {
                return BadRequest(new { success = false, message = "Failed to deserialize model" });
            }
        }
        catch (JsonException jsonEx)
        {
            return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
        }
        
        model.UserName = model.UserName ?? "Kedves Felhasználó";
        model.Email = model.Email ?? "no-email@example.com";
        model.VerificationCode = model.VerificationCode ?? "123456";
        
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("AETHRA", _smtpUsername));
        message.To.Add(new MailboxAddress(model.UserName, model.Email));
        message.Subject = $"🔐 Jelszó módosítás megerősítése - AETHRA";
        
        var emailBody = BuildPasswordChangeVerificationEmail(model);
        message.Body = new TextPart("html") { Text = emailBody };
        
        var emailSent = await SendEmailAsync(message);
        
        if (emailSent)
        {
            return Ok(new { success = true, message = "Password change verification email sent successfully!" });
        }
        
        return StatusCode(500, new { success = false, message = "Failed to send email" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Hiba: {ex.Message}");
        return StatusCode(500, new { success = false, message = ex.Message });
    }
}

// Sikeres jelszó módosítás értesítő email
[HttpPost("SendPasswordChangeSuccess")]
public async Task<IActionResult> SendPasswordChangeSuccess()
{
    try
    {
        Console.WriteLine($"📧 Jelszó módosítás sikeres email küldése: {DateTime.Now}");
        
        string rawBody;
        using (var reader = new StreamReader(Request.Body))
        {
            rawBody = await reader.ReadToEndAsync();
        }
        
        if (string.IsNullOrEmpty(rawBody))
        {
            return BadRequest(new { success = false, message = "Empty request body" });
        }
        
        PasswordChangeSuccessModel? model;
        try
        {
            model = JsonConvert.DeserializeObject<PasswordChangeSuccessModel>(rawBody);
            if (model == null)
            {
                return BadRequest(new { success = false, message = "Failed to deserialize model" });
            }
        }
        catch (JsonException jsonEx)
        {
            return BadRequest(new { success = false, message = $"JSON parse error: {jsonEx.Message}" });
        }
        
        model.UserName = model.UserName ?? "Kedves Felhasználó";
        model.Email = model.Email ?? "no-email@example.com";
        
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("AETHRA", _smtpUsername));
        message.To.Add(new MailboxAddress(model.UserName, model.Email));
        message.Subject = $"✅ Jelszavad sikeresen megváltozott - AETHRA";
        
        var emailBody = BuildPasswordChangeSuccessEmail(model);
        message.Body = new TextPart("html") { Text = emailBody };
        
        var emailSent = await SendEmailAsync(message);
        
        if (emailSent)
        {
            return Ok(new { success = true, message = "Password change success email sent successfully!" });
        }
        
        return StatusCode(500, new { success = false, message = "Failed to send email" });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"❌ Hiba: {ex.Message}");
        return StatusCode(500, new { success = false, message = ex.Message });
    }
}

// Email template-ek
private string BuildPasswordChangeVerificationEmail(PasswordChangeVerificationModel model)
{
    var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Jelszó módosítás megerősítése</title>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f5f5f5; }}
        .container {{ background: white; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: linear-gradient(135deg, #f59e0b 0%, #d97706 100%); color: white; padding: 30px; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 24px; }}
        .content {{ padding: 30px; }}
        .verification-code {{ background: #f8fafc; border: 2px dashed #f59e0b; border-radius: 8px; padding: 25px; margin: 25px 0; text-align: center; font-family: monospace; font-size: 32px; font-weight: bold; letter-spacing: 5px; }}
        .warning {{ background: #fef3c7; border-left: 4px solid #f59e0b; padding: 15px; margin: 20px 0; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 12px; border-top: 1px solid #e2e8f0; padding-top: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>🔐 Jelszó módosítás</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{model.UserName}</strong>!</p>
            <p>Kérted a jelszavad módosítását. A módosítás befejezéséhez add meg az alábbi kódot:</p>
            
            <div class='verification-code'>{model.VerificationCode}</div>
            
            <div class='warning'>
                <strong>⚠️ Fontos!</strong>
                <ul style='margin: 10px 0 0 20px;'>
                    <li>A kód <strong>15 percig</strong> érvényes</li>
                    <li>Ne add meg ezt a kódot senkinek!</li>
                    <li>Ha nem te kezdeményezted a jelszó módosítást, hagyd figyelmen kívül ezt az emailt</li>
                </ul>
            </div>
            
            <p style='margin-top: 25px;'>Üdvözlettel,<br><strong>AETHRA Étterem Csapata</strong></p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
        </div>
    </div>
</body>
</html>";
    return emailHtml;
}

private string BuildPasswordChangeSuccessEmail(PasswordChangeSuccessModel model)
{
    var emailHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>Jelszó módosítás megerősítése</title>
    <style>
        body {{ font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; line-height: 1.6; color: #333; max-width: 600px; margin: 0 auto; padding: 20px; background-color: #f5f5f5; }}
        .container {{ background: white; border-radius: 10px; overflow: hidden; box-shadow: 0 4px 12px rgba(0,0,0,0.1); }}
        .header {{ background: linear-gradient(135deg, #10b981 0%, #059669 100%); color: white; padding: 30px; text-align: center; }}
        .header h1 {{ margin: 0; font-size: 24px; }}
        .content {{ padding: 30px; }}
        .success-box {{ background: #f0fdf4; border-left: 4px solid #10b981; padding: 20px; margin: 20px 0; text-align: center; }}
        .footer {{ text-align: center; margin-top: 30px; color: #666; font-size: 12px; border-top: 1px solid #e2e8f0; padding-top: 20px; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h1>✅ Jelszó módosítás</h1>
        </div>
        <div class='content'>
            <p>Tisztelt <strong>{model.UserName}</strong>!</p>
            
            <div class='success-box'>
                <i class='bi bi-check-circle-fill' style='font-size: 48px; color: #10b981;'></i>
                <p style='font-size: 18px; margin-top: 10px;'>Jelszavad sikeresen megváltozott!</p>
            </div>
            
            <p>Ha nem te végezted ezt a módosítást, kérjük, azonnal lépj kapcsolatba ügyfélszolgálatunkkal!</p>
            
            <p>Üdvözlettel,<br><strong>AETHRA Étterem Csapata</strong></p>
        </div>
        <div class='footer'>
            <p>&copy; {DateTime.Now.Year} AETHRA Étterem. Minden jog fenntartva.</p>
        </div>
    </div>
</body>
</html>";
    return emailHtml;
}

// Modellek
public class PasswordChangeVerificationModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? VerificationCode { get; set; }
}

public class PasswordChangeSuccessModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
}
}


// MODELLEK - nullable típusokkal
public class OrderConfirmationModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? OrderId { get; set; }
    public List<OrderItem>? Items { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal ServiceFee { get; set; }
    public string? ReservationId { get; set; }
    public ReservationDetails? Reservation { get; set; }
    public string? ConsumptionMode { get; set; }
    public string? OrderDate { get; set; }
    public string? Notes { get; set; }
}

public class OrderItem
{
    public string? Name { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class ReservationDetails
{
    public string? ReservationId { get; set; }
    public string? TableName { get; set; }
    public string? TableNumber { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public int? Guests { get; set; }
    public string? TableLocation { get; set; }
    public string? Message { get; set; }
    public string? TableId { get; set; }
}

public class ReservationApprovalModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? ReservationId { get; set; }
    public string? TableName { get; set; }
    public string? TableNumber { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public int? Guests { get; set; }
    public string? TableLocation { get; set; }
    public List<string>? HtmlServices { get; set; }
    public string? Notes { get; set; }
}

public class OrderApprovalModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? OrderId { get; set; }
    public List<OrderItem>? Items { get; set; }
    public decimal TotalAmount { get; set; }
    public string? EstimatedDelivery { get; set; }
    public string? OrderDate { get; set; }
    public string? ReservationId { get; set; }
}

public class OrderDeliveredModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? OrderId { get; set; }
    public string? DeliveryTime { get; set; }
}

public class ReservationRejectionModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? ReservationId { get; set; }
    public string? TableName { get; set; }
    public string? TableNumber { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public int? Guests { get; set; }
    public string? TableLocation { get; set; }
    public string? RejectionDate { get; set; }
    public string? RejectionReason { get; set; }
}

public class OrderRejectionModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? OrderId { get; set; }
    public string? RejectionDate { get; set; }
    public string? OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? RejectionReason { get; set; }
}

public class VerificationEmailModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? VerificationCode { get; set; }
}
public class ReservationReminderModel
{
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? ReservationId { get; set; }
    public string? TableName { get; set; }
    public string? TableNumber { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public int? Guests { get; set; }
    public string? TableLocation { get; set; }
}