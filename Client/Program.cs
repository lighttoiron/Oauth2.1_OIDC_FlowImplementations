var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// Serves index.html when a directory is requested
// E.g. if a user navigates to localhost:5172/ with no specific file listed, a default document (index.html) in wwwroot will be served
app.UseDefaultFiles();

// Serves any file in wwwroot
// Basically, this allows any file in wwwroot to be served if specifically requested (e.g. http://localhost:5172/index.html will b served if called directly)
app.UseStaticFiles();

app.Run();
