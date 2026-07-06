using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;


// TODO: get new keys from discovery / JWKS if JWKS is reset.  Need to add logic to do this


var builder = WebApplication.CreateBuilder(args);

// Add Http logging so we can see the requests and responses in the console
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});
//

// Add CORS to allow our client app to call this endpoint
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

// Get the ApiOptions from appsettings.json
// ! here indicates to the compiler that we guarantee this value isn't null, so don't show warnings
var apiOptions = builder.Configuration.GetSection("ResourceApi").Get<ResourceApiOptions>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Authority is our auth server's base URL, the server registered to provide access tokens to this API.
        // The JwtBearer handler will automatically fetch {Authority}/.well-known/openid-configuration,
        //  find the jwks_uri provided there, fetch THAT, and cache the public keys.

        // Notes: Setting options.Authority will trigger automatic discovery, calling GET on the discovery endpoint, getting the jwks endpoint
        //  calling GET on jwks, grabbing the public keys, and caching the keys
        // Every incoming Bearer token will have its kid extracted, compared against these cached keys, and have its signature verified
        // This requires strict naming conventions and policies to be adhered to
        options.Authority = apiOptions.Authority;

        // Audience must match the "aud" claim our /token endpoint set on access tokens.
        // This indicates the auth server has provided the token for THIS specific registered API.
        options.Audience = apiOptions.Audience;

        // Some debugging logs to help diagnose problems
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine($"Token validation failed: {context.Exception.GetType().Name} - {context.Exception.Message}");
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Console.WriteLine($"Auth challenge issued: {context.Error} - {context.ErrorDescription}");
                return Task.CompletedTask;
            }
            
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Add logging for improved debugging locally
app.UseHttpLogging();

// CORS must come before UseAuthentication and UseAuthorization
// This is because when a message with a custom header (like Authorization: Bearer ...) is sent over CORS,
//  an OPTIONS preflight call is made first, without that header.  If we reject the OPTIONS call since it can't be authenticated,
//  our client will be unable to call this API
app.UseCors("ClientApp");

// NOTE:: IMPORTANT: app.UseAuthentication() must be called BEFORE app.UseAuthorization() or auth requests will fail.
// This is because the middleware pipeline is an ordered chain, and all app.Use...() calls will be effectuated in order
// Each element of the chain gets a chance to inspect and then accept or reject the call in the order they were set up
// 
// Looks at the incoming request, reads the Authorization header (in our case, since we set up AddJwtBearer as our default
// authentication handler), validates the JWT, then populates HttpContext.USer with a ClaimsPrinciple built from the token's claiims
app.UseAuthentication();
// Looks at HttpContext.User and decides (based on policies like .RequireAuthorization) whether the user provided is permitted
//  to access the requested endpoint
app.UseAuthorization();



app.MapGet("/", () => "Hello World!");


// Note: .RequireAuthorization() protects this endpoint.  Middleware will intercept all calls to this endpoint,
//  extract their Authorization: Bearer <token bits> header, and run a full validation check (signature, issuer, audience, expiry)
//  before allowing access to this endpoint and invoking the handler.  Any failure returns a 401.
// NOTE: since we cache the JWKS keys, if the jwks server is restarted, we will still need to fetch new keys and refresh our cache
app.MapGet("/protected", (ClaimsPrincipal user) =>
{
   var claims = user.Claims.Select(c => new { c.Type, c.Value});
   return Results.Ok(new { message = "Protected resource has been successfully accessed!", claims}); 
}).RequireAuthorization();

app.Run();
