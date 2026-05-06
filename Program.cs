
using SignalRChat.Hubs;
// using YourProjectNamespace.Data; // <- ide a saját DbContext namespace-ed

var builder = WebApplication.CreateBuilder(args);

// MySQL kapcsolat beállítása - EZT ADTAD HOZZÁ


builder.Services.AddControllers();
builder.Services.AddSignalR();

var app = builder.Build();

app.UseStaticFiles();
app.UseRouting();

app.MapGet("/", () => Results.Redirect("/index.html"));
app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.Run();