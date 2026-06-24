var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Serves index.html when a directory is requested
app.UseDefaultFiles();

// Serves any file in wwwroot
app.UseStaticFiles();

app.Run();
