using SignalRChat.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ==================================================
// ADATBÁZIS KAPCSOLAT BEÁLLÍTÁSA
// ==================================================

// A connection string beolvasása az appsettings.json-ból
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// A kapcsolat regisztrálása DI container-be (opcionális, de ajánlott)
builder.Services.AddTransient<MySql.Data.MySqlClient.MySqlConnection>(provider =>
    new MySql.Data.MySqlClient.MySqlConnection(connectionString)
);

builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

// ==================================================
// STATIKUS FÁJLOK ÉS ROUTING
// ==================================================
app.UseStaticFiles();
app.UseRouting();

app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.Run();
