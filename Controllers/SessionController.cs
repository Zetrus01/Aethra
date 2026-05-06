using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using KrajcsovicsChristoferHtml.Utils;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using MySql.Data.MySqlClient;

[Route("[controller]")]
[ApiController]
public class SessionController : ControllerBase
{
    private readonly string? _connectionString;
    private readonly ILogger<SessionController> _logger;
    private readonly TimeSpan _sessionExpiry = TimeSpan.FromDays(7);
    
    // Email verifikáció tároló
    private static readonly ConcurrentDictionary<string, VerificationData> PendingVerifications = new ConcurrentDictionary<string, VerificationData>();

    public SessionController(IConfiguration configuration, ILogger<SessionController> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
        _logger = logger;
    }

    [HttpPost("SignUp")]
    public async Task<IActionResult> SignUp([FromBody] SignUpModel model)
    {
        if (model == null || string.IsNullOrWhiteSpace(model.UserName) || string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.UserPassword))
        {
            return BadRequest(new { success = false, message = "Felhasználónév, e-mail cím és jelszó megadása kötelező." });
        }

        // E-mail validáció
        if (!IsValidEmail(model.Email))
        {
            return BadRequest(new { success = false, message = "Érvénytelen e-mail formátum." });
        }

        // Jelszó hossz ellenőrzése
        if (model.UserPassword.Length < 6)
        {
            return BadRequest(new { success = false, message = "A jelszónak legalább 6 karakter hosszúnak kell lennie." });
        }

        try
        {
            if (await UserNameExistsAsync(model.UserName))
            {
                return Conflict(new { success = false, message = "A felhasználónév már foglalt." });
            }

            if (await EmailExistsAsync(model.Email))
            {
                return Conflict(new { success = false, message = "Ez az e-mail cím már regisztrálva van." });
            }

            // Távolítsuk el a régi verifikációt
            PendingVerifications.TryRemove(model.Email, out _);

            // Generáljunk verifikációs kódot
            var verificationCode = new Random().Next(100000, 999999).ToString();
            _logger.LogInformation("Verifikációs kód generálva: {VerificationCode} for {Email}", verificationCode, model.Email);

            // Mentsük el a verifikációs adatokat
            var verificationData = new VerificationData
            {
                UserName = model.UserName,
                Email = model.Email,
                UserPassword = model.UserPassword,
                VerificationCode = verificationCode,
                Expiry = DateTime.Now.AddMinutes(15)
            };

            PendingVerifications[model.Email] = verificationData;

            // Küldjük el a verifikációs emailt
            var emailResult = await SendVerificationEmailAsync(model.UserName, model.Email, verificationCode);
            
            if (!emailResult)
            {
                PendingVerifications.TryRemove(model.Email, out _);
                return BadRequest(new { success = false, message = "Nem sikerült elküldeni a megerősítő emailt. Kérjük, próbálja újra." });
            }

            _logger.LogInformation("Verifikációs email elküldve: {Email}", model.Email);

            return Ok(new { 
                success = true, 
                message = "Megerősítő emailt küldtünk a megadott címre. Kérjük, ellenőrizze emailjeit!",
                requiresVerification = true,
                email = MaskEmail(model.Email)
            });

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hiba regisztráció közben: {UserName} e-mail: {Email}", model.UserName, model.Email);
            return StatusCode(500, new { success = false, message = "Hiba történt a regisztráció során." });
        }
    }

    [HttpPost("VerifyEmail")]
    public async Task<IActionResult> VerifyEmail([FromBody] EmailVerificationModel model)
    {
        try
        {
            _logger.LogInformation("Email megerősítés: {Email}, Kód: {VerificationCode}", model.Email, model.VerificationCode);

            // Ellenőrizzük, hogy van-e ilyen verifikáció
            if (!PendingVerifications.ContainsKey(model.Email))
            {
                _logger.LogWarning("Nincs verifikáció: {Email}", model.Email);
                return BadRequest(new { success = false, message = "Nincs függőben lévő megerősítés. Kérjük, regisztráljon újra." });
            }

            var verificationData = PendingVerifications[model.Email];

            // Ellenőrizzük, hogy lejárt-e
            if (DateTime.Now > verificationData.Expiry)
            {
                PendingVerifications.TryRemove(model.Email, out _);
                _logger.LogWarning("Lejárt kód: {Email}", model.Email);
                return BadRequest(new { success = false, message = "A megerősítő kód lejárt. Kérjük, regisztráljon újra." });
            }

            // Kód ellenőrzése
            if (verificationData.VerificationCode != model.VerificationCode?.Trim())
            {
                _logger.LogWarning("Hibás kód: {VerificationCode} (várt: {ExpectedCode})", model.VerificationCode, verificationData.VerificationCode);
                return BadRequest(new { success = false, message = "Hibás megerősítő kód!" });
            }

            // ✅ SIKERES MEGERŐSÍTÉS - Ellenőrizzük, hogy még mindig szabad-e a felhasználónév/email
            _logger.LogInformation("Sikeres kód ellenőrzés: {Email}", model.Email);

            if (await UserNameExistsAsync(verificationData.UserName))
            {
                _logger.LogWarning("Felhasználónév már foglalt verifikáció után: {UserName}", verificationData.UserName);
                PendingVerifications.TryRemove(model.Email, out _);
                return BadRequest(new { success = false, message = "A felhasználónév már foglalt. Kérjük, regisztráljon újra." });
            }

            if (await EmailExistsAsync(verificationData.Email))
            {
                _logger.LogWarning("Email már foglalt verifikáció után: {Email}", verificationData.Email);
                PendingVerifications.TryRemove(model.Email, out _);
                return BadRequest(new { success = false, message = "Az email cím már foglalt. Kérjük, regisztráljon újra." });
            }

            // Felhasználó létrehozása
            string salt = PasswordProcessor.GenerateRandomSequence(16);
            string hash = PasswordProcessor.PasswordHash(verificationData.UserPassword, salt);

            await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                // Insert into User table
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO User (UserName, Email, UserPassword) 
                    VALUES (@UserName, @Email, @UserPassword)";
                command.Parameters.AddWithValue("@UserName", verificationData.UserName);
                command.Parameters.AddWithValue("@Email", verificationData.Email);
                command.Parameters.AddWithValue("@UserPassword", hash);
                int userRows = await command.ExecuteNonQueryAsync();
                _logger.LogInformation("User tábla insert: {Rows} sor befolyásolva", userRows);

                // Insert into UserAuth table
                command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO UserAuth (UserName, UserSalt, UserHash) 
                    VALUES (@UserName, @UserSalt, @UserHash)";
                command.Parameters.AddWithValue("@UserName", verificationData.UserName);
                command.Parameters.AddWithValue("@UserSalt", salt);
                command.Parameters.AddWithValue("@UserHash", hash);
                int authRows = await command.ExecuteNonQueryAsync();
                _logger.LogInformation("UserAuth tábla insert: {Rows} sor befolyásolva", authRows);

                await transaction.CommitAsync();

                _logger.LogInformation("Felhasználó létrehozva: {UserName}, {Email}", verificationData.UserName, verificationData.Email);

                // ✅ NEM hozunk létre sessiont, NEM állítunk be cookie-t
                // ✅ Csak eltávolítjuk a verifikációt és visszaküldjük a siker üzenetet

                // Távolítsuk el a verifikációt
                PendingVerifications.TryRemove(model.Email, out _);

                _logger.LogInformation("Sikeres regisztráció (nincs automatikus bejelentkezés): {Email}", verificationData.Email);

                return Ok(new { 
                    success = true, 
                    message = "Sikeres regisztráció! Most már bejelentkezhet a bejelentkezési oldalon.",
                    userName = verificationData.UserName,
                    redirectUrl = "/login.html" // ✅ Átirányítás a bejelentkezési oldalra
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Tranzakció hiba verifikáció közben: {UserName}", verificationData.UserName);
                
                // Részletesebb hibaüzenet
                string errorMessage = ex.Message.Contains("UNIQUE constraint failed") 
                    ? "A felhasználónév vagy email cím már foglalt." 
                    : "Adatbázis hiba történt a felhasználó létrehozásakor.";
                
                PendingVerifications.TryRemove(model.Email, out _);
                return BadRequest(new { success = false, message = errorMessage });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VERIFIKÁCIÓS HIBA: {Email}", model.Email);
            return BadRequest(new { success = false, message = "Hiba történt a megerősítés során." });
        }
    }

    [HttpPost("ResendVerification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationModel model)
    {
        try
        {
            _logger.LogInformation("Új kód kérése: {Email}", model.Email);

            if (!PendingVerifications.ContainsKey(model.Email))
                return BadRequest(new { success = false, message = "Nincs függőben lévő megerősítés." });

            var oldData = PendingVerifications[model.Email];
            
            // Ellenőrizzük, hogy nem kér-e túl gyakran új kódot
            if (DateTime.Now < oldData.CreatedAt.AddMinutes(1))
            {
                return BadRequest(new { success = false, message = "Kérjük, várjon legalább 1 percet az új kód kérése között." });
            }

            // Új kód generálása
            var newCode = new Random().Next(100000, 999999).ToString();
            oldData.VerificationCode = newCode;
            oldData.Expiry = DateTime.Now.AddMinutes(15);
            oldData.CreatedAt = DateTime.Now;

            _logger.LogInformation("Új kód generálva: {NewCode}", newCode);

            // Új email küldése
            var emailResult = await SendVerificationEmailAsync(oldData.UserName, model.Email, newCode);

            if (!emailResult)
                return BadRequest(new { success = false, message = "Nem sikerült elküldeni az emailt." });

            return Ok(new { 
                success = true, 
                message = "Új kódot küldtünk!"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Újraküldési hiba: {Email}", model.Email);
            return BadRequest(new { success = false, message = "Hiba történt." });
        }
    }

    // Login - támogatja mind a Form, mind a JSON bemenetet
    [HttpPost("Login")]
    public async Task<IActionResult> Login()
    {
        try
        {
            LoginModel? model;

            // Ellenőrizzük, hogy JSON vagy Form adatot kaptunk-e
            if (Request.ContentType?.Contains("application/json") == true)
            {
                model = await JsonSerializer.DeserializeAsync<LoginModel>(Request.Body);
            }
            else
            {
                // Form adatok
                var form = await Request.ReadFormAsync();
                model = new LoginModel
                {
                    UserName = form["UserName"]!,
                    UserPassword = form["UserPassword"]!
                };
            }

            if (model == null || string.IsNullOrWhiteSpace(model.UserName) || string.IsNullOrWhiteSpace(model.UserPassword))
            {
                return BadRequest(new { success = false, message = "Felhasználónév és jelszó megadása kötelező." });
            }

            if (!await AuthorizeUserAsync(model.UserName, model.UserPassword))
            {
                _logger.LogWarning("Failed login attempt for user: {UserName}", model.UserName);
                Response.Cookies.Delete("SessionID");
                return Unauthorized(new { success = false, message = "Helytelen felhasználónév vagy jelszó." });
            }

            string sessionId = await CreateSessionAsync(model.UserName);

            Response.Cookies.Append("SessionID", sessionId, new CookieOptions
            {
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                HttpOnly = true,
                Expires = DateTimeOffset.UtcNow.Add(_sessionExpiry)
            });

            _logger.LogInformation("User {UserName} logged in successfully", model.UserName);
            return Ok(new { success = true, message = "Bejelentkezés sikeres." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login");
            return StatusCode(500, new { success = false, message = "Hiba történt a bejelentkezés során." });
        }
    }

    // Alternatív Login endpoint csak Form adatokhoz (a régi login.html kompatibilitás)
    [HttpPost("LoginForm")]
    public async Task<IActionResult> LoginForm([FromForm] string UserName, [FromForm] string UserPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(UserPassword))
            {
                return BadRequest(new { success = false, message = "Felhasználónév és jelszó megadása kötelező." });
            }

            if (!await AuthorizeUserAsync(UserName, UserPassword))
            {
                _logger.LogWarning("Failed login attempt for user: {UserName}", UserName);
                Response.Cookies.Delete("SessionID");
                return Unauthorized(new { success = false, message = "Helytelen felhasználónév vagy jelszó." });
            }

            string sessionId = await CreateSessionAsync(UserName);

            Response.Cookies.Append("SessionID", sessionId, new CookieOptions
            {
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                HttpOnly = true,
                Expires = DateTimeOffset.UtcNow.Add(_sessionExpiry)
            });

            _logger.LogInformation("User {UserName} logged in successfully", UserName);
            return Ok(new { success = true, message = "Bejelentkezés sikeres." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for user: {UserName}", UserName);
            return StatusCode(500, new { success = false, message = "Hiba történt a bejelentkezés során." });
        }
    }

    [HttpPost("Logout")]
    public async Task<IActionResult> Logout()
    {
        var sessionId = Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Ok(new { success = true, message = "Már ki van jelentkezve." });
        }

        try
        {
            // Törlés az adatbázisból
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Session WHERE SessionID = @SessionID";
            command.Parameters.AddWithValue("@SessionID", sessionId);
            await command.ExecuteNonQueryAsync();

            // Cookie törlése
            Response.Cookies.Delete("SessionID");

            _logger.LogInformation("Felhasználó kijelentkezett és törlődött a session: {SessionId}", sessionId);
            return Ok(new { success = true, message = "Kijelentkezés sikeres." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout for session: {SessionId}", sessionId);
            return StatusCode(500, new { success = false, message = "Hiba történt a kijelentkezés során." });
        }
    }

    [HttpGet("GetUserId")]
    public async Task<IActionResult> GetUserId()
    {
        var sessionId = Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Ok(new { 
                userName = "",
                email = "",
                isAuthenticated = false
            });
        }

        try
        {
            string? userName = await GetLoggedInUserAsync(sessionId);
            if (string.IsNullOrEmpty(userName))
            {
                Response.Cookies.Delete("SessionID");
                return Ok(new { 
                    userName = "",
                    email = "",
                    isAuthenticated = false
                });
            }

            // E-mail cím lekérése az adatbázisból
            string email = await GetUserEmailAsync(userName);
            
            _logger.LogDebug("Retrieved user ID for session: {SessionId}", sessionId);
            return Ok(new { 
                userName = userName,
                email = email,
                isAuthenticated = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user ID for session: {SessionId}", sessionId);
            return Ok(new { 
                userName = "",
                email = "",
                isAuthenticated = false
            });
        }
    }
[HttpGet("GetUserRoles")]
public async Task<IActionResult> GetUserRoles()
{
    var sessionId = Request.Cookies["SessionID"];
    if (string.IsNullOrEmpty(sessionId))
        return Ok(new { roles = new[] { "guest" } });
    
    var userName = await GetLoggedInUserAsync(sessionId);
    if (string.IsNullOrEmpty(userName))
        return Ok(new { roles = new[] { "guest" } });
    
    var roles = await GetUserRolesFromDatabase(userName);
    return Ok(new { roles = roles });
}

private async Task<string[]> GetUserRolesFromDatabase(string userName)
{
    try
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var roles = new List<string>();
        
        // Lekérjük a felhasználó szerepkörét az új UserRoles táblából
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT r.Name 
            FROM UserRoles ur
            JOIN Roles r ON ur.RoleId = r.Id
            WHERE ur.UserId = @UserName";
        command.Parameters.AddWithValue("@UserName", userName);
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            roles.Add(reader.GetString(0));
        }
        
        // Ha nincs szerepkör, alapértelmezett 'user'
        if (roles.Count == 0)
        {
            roles.Add("user");
        }
        
        _logger.LogInformation("Szerepkörök lekérve a felhasználóhoz {UserName}: {Roles}", 
            userName, string.Join(", ", roles));
        
        return roles.ToArray();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a szerepkörök lekérése közben: {UserName}", userName);
        return new[] { "user" };
    }
}
[HttpGet("GetUserPermissions")]
public async Task<IActionResult> GetUserPermissions()
{
    var sessionId = Request.Cookies["SessionID"];
    if (string.IsNullOrEmpty(sessionId))
        return Ok(new { permissions = GetGuestPermissions() });
    
    var userName = await GetLoggedInUserAsync(sessionId);
    if (string.IsNullOrEmpty(userName))
        return Ok(new { permissions = GetGuestPermissions() });
    
    var permissions = await GetUserPermissionsFromDatabase(userName);
    return Ok(new { permissions = permissions });
}

private async Task<string[]> GetUserPermissionsFromDatabase(string userName)
{
    try
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        
        var permissions = new List<string>();
        
        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT DISTINCT p.Name 
            FROM User u
            JOIN UserRoles ur ON u.UserName = ur.UserId
            JOIN RolePermissions rp ON ur.RoleId = rp.RoleId
            JOIN Permissions p ON rp.PermissionId = p.Id
            WHERE u.UserName = @UserName";
        command.Parameters.AddWithValue("@UserName", userName);
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            permissions.Add(reader.GetString(0));
        }
        
        return permissions.ToArray();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a jogosultságok lekérése közben: {UserName}", userName);
        return GetGuestPermissions();
    }
}

private string[] GetGuestPermissions()
{
    return new[] { "product.view", "reservation.create", "cart.view", "cart.edit" };
}


// Helper metódus az admin státusz lekéréséhez 
private async Task<bool> IsUserAdminAsync(string userName)
{
    try
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT IsAdmin FROM User WHERE UserName = @UserName";
        command.Parameters.AddWithValue("@UserName", userName);

        var result = await command.ExecuteScalarAsync();
        
        if (result != null && result != DBNull.Value)
        {
            return Convert.ToBoolean(result);
        }
        
        return false;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba az admin státusz lekérésekor: {UserName}", userName);
        return false;
    }
}

    #region Private Helper Methods

    private async Task<string> CreateSessionAsync(string userName)
    {
        string sessionId = PasswordProcessor.GenerateRandomSequence(20);
        long expiryTime = DateTimeOffset.UtcNow.Add(_sessionExpiry).ToUnixTimeSeconds();

        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO Session (SessionID, CreationTime, ExpiryTime, UserName) 
            VALUES (@SessionID, @CreationTime, @ExpiryTime, @UserName)";
        command.Parameters.AddWithValue("@SessionID", sessionId);
        command.Parameters.AddWithValue("@CreationTime", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("@ExpiryTime", expiryTime);
        command.Parameters.AddWithValue("@UserName", userName);

        await command.ExecuteNonQueryAsync();

        return sessionId;
    }

    private async Task<string?> GetLoggedInUserAsync(string sessionId)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT UserName FROM Session 
            WHERE SessionID = @SessionID 
            AND ExpiryTime > @CurrentTime";
        command.Parameters.AddWithValue("@SessionID", sessionId);
        command.Parameters.AddWithValue("@CurrentTime", DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var result = await command.ExecuteScalarAsync();
        return result?.ToString();
    }

    private async Task<string> GetUserEmailAsync(string userName)
    {
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Email FROM User WHERE UserName = @UserName";
        command.Parameters.AddWithValue("@UserName", userName);

        var result = await command.ExecuteScalarAsync();
        return result?.ToString() ?? string.Empty;
    }

    private async Task<bool> UserNameExistsAsync(string userName)
    {
        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString ?? "");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM User WHERE UserName = @UserName LIMIT 1";
        command.Parameters.AddWithValue("@UserName", userName);

        return await command.ExecuteScalarAsync() != null;
    }

    private async Task<bool> EmailExistsAsync(string email)
    {
        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString ?? "");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM User WHERE Email = @Email LIMIT 1";
        command.Parameters.AddWithValue("@Email", email);

        return await command.ExecuteScalarAsync() != null;
    }

    private async Task<bool> AuthorizeUserAsync(string userName, string challengePassword)
    {
        var authData = await GetUserAuthDataAsync(userName);
        if (authData == null) 
        {
            _logger.LogWarning("Nincs auth data a felhasználónak: {UserName}", userName);
            return false;
        }

        bool result = PasswordProcessor.VerifyPassword(challengePassword, authData.Value.Salt, authData.Value.Hash);
        _logger.LogInformation("Jelszó ellenőrzés: {UserName} - {Result}", userName, result);
        return result;
    }

    private async Task<(string Salt, string Hash)?> GetUserAuthDataAsync(string userName)
    {
        await using var connection = new MySql.Data.MySqlClient.MySqlConnection(_connectionString ?? "");
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = "SELECT UserSalt, UserHash FROM UserAuth WHERE UserName = @UserName";
        command.Parameters.AddWithValue("@UserName", userName);

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            string salt = reader.GetString(0);
            string hash = reader.GetString(1);
            _logger.LogInformation("Auth data lekérve: {UserName} - Salt: {SaltLength}, Hash: {HashLength}", 
                userName, salt.Length, hash.Length);
            return (salt, hash);
        }

        _logger.LogWarning("Nincs auth data: {UserName}", userName);
        return null;
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> SendVerificationEmailAsync(string userName, string email, string verificationCode)
    {
        try
        {
            using var httpClient = new HttpClient();
            var emailData = new { 
                UserName = userName, 
                Email = email,
                VerificationCode = verificationCode
            };
            
            var response = await httpClient.PostAsJsonAsync($"{Request.Scheme}://{Request.Host}/Email/SendVerificationEmail", emailData);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email küldési hiba: {Email}", email);
            return false;
        }
    }

    private string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            return "***@***.***";

        var parts = email.Split('@');
        if (parts.Length != 2) return "***@***.***";

        var username = parts[0];
        return username.Length <= 2 
            ? new string('*', username.Length) + "@" + parts[1]
            : username.Substring(0, 2) + new string('*', username.Length - 2) + "@" + parts[1];
    }
// Jelszó módosítás kérés - email küldés
[HttpPost("RequestPasswordChange")]
public async Task<IActionResult> RequestPasswordChange([FromBody] PasswordChangeRequestModel model)
{
    try
    {
        var sessionId = Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs bejelentkezve!" });
        }

        var userName = await GetLoggedInUserAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Session érvénytelen!" });
        }

        // Ellenőrizzük a jelenlegi jelszót
        if (!await AuthorizeUserAsync(userName, model.CurrentPassword))
        {
            return BadRequest(new { success = false, message = "A jelenlegi jelszó helytelen!" });
        }

        // Ellenőrizzük az új jelszó hosszát
        if (string.IsNullOrEmpty(model.NewPassword) || model.NewPassword.Length < 6)
        {
            return BadRequest(new { success = false, message = "Az új jelszónak legalább 6 karakter hosszúnak kell lennie!" });
        }

        // Generáljunk verifikációs kódot
        var verificationCode = new Random().Next(100000, 999999).ToString();
        
        // Tároljuk a kódot és az új jelszót (15 percig)
        var passwordChangeData = new PasswordChangeData
        {
            UserName = userName,
            NewPassword = model.NewPassword,
            VerificationCode = verificationCode,
            Expiry = DateTime.Now.AddMinutes(15)
        };
        
        PendingPasswordChanges[userName] = passwordChangeData;

        // E-mail cím lekérése
        var email = await GetUserEmailAsync(userName);

        // Email küldése
        var emailResult = await SendPasswordChangeVerificationEmail(userName, email, verificationCode);

        if (!emailResult)
        {
            PendingPasswordChanges.TryRemove(userName, out _);
            return BadRequest(new { success = false, message = "Nem sikerült elküldeni a megerősítő emailt!" });
        }

        _logger.LogInformation("Jelszó módosítási kérés elküldve: {UserName}", userName);
        
        return Ok(new { 
            success = true, 
            message = "Megerősítő kódot küldtünk az email címedre!",
            email = MaskEmail(email)
        });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a jelszó módosítás kérésnél");
        return StatusCode(500, new { success = false, message = "Hiba történt!" });
    }
}

// Jelszó módosítás megerősítése kóddal
[HttpPost("ConfirmPasswordChange")]
public async Task<IActionResult> ConfirmPasswordChange([FromBody] PasswordChangeConfirmModel model)
{
    try
    {
        var sessionId = Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs bejelentkezve!" });
        }

        var userName = await GetLoggedInUserAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Session érvénytelen!" });
        }

        // Ellenőrizzük, hogy van-e függőben lévő módosítás
        if (!PendingPasswordChanges.ContainsKey(userName))
        {
            return BadRequest(new { success = false, message = "Nincs függőben lévő jelszó módosítás!" });
        }

        var passwordData = PendingPasswordChanges[userName];

        // Ellenőrizzük a kódot
        if (passwordData.VerificationCode != model.VerificationCode?.Trim())
        {
            return BadRequest(new { success = false, message = "Hibás megerősítő kód!" });
        }

        // Ellenőrizzük, hogy nem járt-e le
        if (DateTime.Now > passwordData.Expiry)
        {
            PendingPasswordChanges.TryRemove(userName, out _);
            return BadRequest(new { success = false, message = "A kód lejárt! Kérjük, kezdje újra a folyamatot." });
        }

        // Új jelszó hashelése
        string salt = PasswordProcessor.GenerateSalt(16);
        string hash = PasswordProcessor.PasswordHash(passwordData.NewPassword, salt);

        // Jelszó frissítése az adatbázisban
        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandText = @"
            UPDATE UserAuth 
            SET UserSalt = @UserSalt, UserHash = @UserHash 
            WHERE UserName = @UserName";
        command.Parameters.AddWithValue("@UserSalt", salt);
        command.Parameters.AddWithValue("@UserHash", hash);
        command.Parameters.AddWithValue("@UserName", userName);
        
        await command.ExecuteNonQueryAsync();

        // Távolítsuk el a függőben lévő módosítást
        PendingPasswordChanges.TryRemove(userName, out _);

        _logger.LogInformation("Jelszó sikeresen módosítva: {UserName}", userName);

        // Sikeres módosítás email küldése
        var userEmail = await GetUserEmailAsync(userName);
        await SendPasswordChangeSuccessEmail(userName, userEmail);

        return Ok(new { success = true, message = "Jelszó sikeresen módosítva!" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba a jelszó módosítás megerősítésénél");
        return StatusCode(500, new { success = false, message = "Hiba történt!" });
    }
}

// Újraküldés
[HttpPost("ResendPasswordChangeCode")]
public async Task<IActionResult> ResendPasswordChangeCode([FromBody] ResendPasswordChangeModel model)
{
    try
    {
        var sessionId = Request.Cookies["SessionID"];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { success = false, message = "Nincs bejelentkezve!" });
        }

        var userName = await GetLoggedInUserAsync(sessionId);
        if (string.IsNullOrEmpty(userName))
        {
            return Unauthorized(new { success = false, message = "Session érvénytelen!" });
        }

        if (!PendingPasswordChanges.ContainsKey(userName))
        {
            return BadRequest(new { success = false, message = "Nincs függőben lévő jelszó módosítás!" });
        }

        var existingData = PendingPasswordChanges[userName];
        
        // Ellenőrizzük, hogy nem kér-e túl gyakran új kódot
        if (DateTime.Now < existingData.CreatedAt.AddMinutes(1))
        {
            return BadRequest(new { success = false, message = "Kérjük, várjon legalább 1 percet az új kód kérése között!" });
        }

        // Új kód generálása
        var newCode = new Random().Next(100000, 999999).ToString();
        existingData.VerificationCode = newCode;
        existingData.Expiry = DateTime.Now.AddMinutes(15);
        existingData.CreatedAt = DateTime.Now;

        var email = await GetUserEmailAsync(userName);
        var emailResult = await SendPasswordChangeVerificationEmail(userName, email, newCode);

        if (!emailResult)
        {
            return BadRequest(new { success = false, message = "Nem sikerült elküldeni az emailt!" });
        }

        return Ok(new { success = true, message = "Új kódot küldtünk az email címedre!" });
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Hiba az újraküldésnél");
        return StatusCode(500, new { success = false, message = "Hiba történt!" });
    }
}

// Email küldő metódusok
private async Task<bool> SendPasswordChangeVerificationEmail(string userName, string email, string verificationCode)
{
    try
    {
        using var httpClient = new HttpClient();
        var emailData = new
        {
            UserName = userName,
            Email = email,
            VerificationCode = verificationCode
        };
        
        var response = await httpClient.PostAsJsonAsync($"{Request.Scheme}://{Request.Host}/Email/SendPasswordChangeVerification", emailData);
        return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Email küldési hiba: {Email}", email);
        return false;
    }
}

private async Task<bool> SendPasswordChangeSuccessEmail(string userName, string email)
{
    try
    {
        using var httpClient = new HttpClient();
        var emailData = new
        {
            UserName = userName,
            Email = email
        };
        
        var response = await httpClient.PostAsJsonAsync($"{Request.Scheme}://{Request.Host}/Email/SendPasswordChangeSuccess", emailData);
        return response.IsSuccessStatusCode;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Email küldési hiba: {Email}", email);
        return false;
    }
}
    #endregion

    #region Models

    public class SignUpModel
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserPassword { get; set; } = string.Empty;
    }

    public class LoginModel
    {
        public string UserName { get; set; } = string.Empty;
        public string UserPassword { get; set; } = string.Empty;
    }

    public class EmailVerificationModel
    {
        public string Email { get; set; } = string.Empty;
        public string VerificationCode { get; set; } = string.Empty;
    }

    public class ResendVerificationModel
    {
        public string Email { get; set; } = string.Empty;
    }

    public class VerificationData
    {
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string UserPassword { get; set; } = string.Empty;
        public string VerificationCode { get; set; } = string.Empty;
        public DateTime Expiry { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
public class PasswordChangeRequestModel
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class PasswordChangeConfirmModel
{
    public string VerificationCode { get; set; } = string.Empty;
}

public class ResendPasswordChangeModel { }

public class PasswordChangeData
{
    public string UserName { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string VerificationCode { get; set; } = string.Empty;
    public DateTime Expiry { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

// Tároló a függőben lévő jelszó módosításokhoz
private static readonly ConcurrentDictionary<string, PasswordChangeData> PendingPasswordChanges = new ConcurrentDictionary<string, PasswordChangeData>();
    #endregion
}