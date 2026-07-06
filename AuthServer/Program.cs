using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

// TODO: add a refresh token to the response and the client app
// TODO: add a client ID and client secret to our BFF backend so we are a registered client
// TODO: convert our page to a web components based model with multiple tabs for different auth flows
// TODO: add a backend database of some kind

var builder = WebApplication.CreateBuilder(args);

// Add Http logging so we can see the requests and responses in the console
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});
//

// Add CORS policy to allow cross origin requests from our client JS
// This needs to be configured for each other origin that we want to allow to call this endpoint
// Note that a SPA operating with a BFF architecture does not call this endpoint directly and does not need to allow CORS requests
builder.Services.AddCors(options =>
{
    options.AddPolicy("ClientApp", policy =>
    {
        policy.WithOrigins("http://localhost:7000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
//

// Register our cleanup service to remove old pending auth requests that were never fulfilled
// AddHostedService will automatically call PendingRequestCleanupService.ExecuteAsync, no need to call it yourself
builder.Services.AddHostedService<PendingRequestCleanupService>();
// Configure our server to automatically provide certain options to any endpoint that needs them when receiving a request
// This happens once, before app.Run().  builder.Services is the Dependency Injection (DI) container we will use to look up needed values later
// Services.Configure tells the DI container how to build an AuthServerOptions object by reading values out of the appsettings.json file.
// Also wraps the returned object so it can be requested as an IOptions<AuthServerOptions>
builder.Services.Configure<AuthServerOptions>(builder.Configuration.GetSection("AuthServer"));
// Services.AddSingleton says (if anyone asks for an ISigningKeyProvider, create exactly one and return that one instance for all future requests)
builder.Services.AddSingleton<ISigningKeyProvider, InMemorySigningKeyProvider>();
// A note: When we pass ISigningKeyProvider or IOptions<AuthServerOptions> into our Map requests later, the ASP.NET Core framework recognizes 
//  that they are registered services and asks the DI container (builder.Services) to provide them
// InMemorySigningKeyProvider is singleton so its only ever built once, we build a new AuthServerOptions each time they are requested
//

var app = builder.Build();

// Set up pretty print JSON capabilities for easy reading when testing
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};
//

// Use CORS to allow the client app to call this endpoint
app.UseCors("ClientApp");
//

// Add the Http logging into the middleware pipeline
app.UseHttpLogging();
//


// Add the jwks (JSON Web Key Set) endpoint, which provides public keys to the client so they can verify the signatures of the JWTs issued by this auth server.
JWKSEndpoint.Map(app, jsonOptions);
//

// Add the Discovery endpoint, which provides info about the auth server to the clients.
DiscoveryEndpoint.Map(app, jsonOptions);
//

// Add the /authorization endpoint
AuthorizeEndpoint.Map(app);
//

// Add the /token endpoint
TokenEndpoint.Map(app);
//


app.Run();