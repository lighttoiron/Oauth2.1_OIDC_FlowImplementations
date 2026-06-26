var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BffOptions>(builder.Configuration.GetSection("Bff"));
builder.Services.AddHttpClient();
builder.Services.AddHostedService<BffCleanupService>();

// Add Http logging so we can see the requests and responses in the console
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});
//

var app = builder.Build();


// Serves index.html when a directory is requested
// E.g. if a user navigates to localhost:5172/ with no specific file listed, a default document (index.html) in wwwroot will be served
app.UseDefaultFiles();

// Serves any file in wwwroot
// Basically, this allows any file in wwwroot to be served if specifically requested (e.g. http://localhost:5172/index.html will b served if called directly)
app.UseStaticFiles();
app.UseHttpLogging();

BffEndpoints.Map(app);

app.Run();
