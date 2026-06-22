using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

// TODO: add a refresh token to the response and the client app
// Add a backend database of some kind

var builder = WebApplication.CreateBuilder(args);

// Add Http logging so we can see the requests and responses in the console
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
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
//  https://localhost:7051/authorize?response_type=code&client_id=test-client&redirect_uri=https://localhost:7051/callback&scope=openid&state=abc123&code_challenge=abc&code_challenge_method=S256
AuthorizeEndpoint.Map(app);
//


app.Run();