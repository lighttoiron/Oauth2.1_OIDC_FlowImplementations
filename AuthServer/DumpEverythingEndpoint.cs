using System.Text.Json;

// An endpoint that dumps all stored server information to the caller
// This is used for this lab only and is not a part of OIDC or OAuth
static class DumpEverythingEndpoint
{
    public static void Map(WebApplication app, JsonSerializerOptions options)
    {
        app.MapGet("/dumpeverything", (HttpContext context) =>
        {
            // Dump everything we have in the auth store and send it to the requester
            var info = new
            {
                ActiveSessions = AuthStore.ActiveSessions,
                PendingRequests = AuthStore.PendingRequests,
                PendingConsentRequests = AuthStore.PendingConsentRequests,
                AuthCodes = AuthStore.AuthCodes,
                RefreshTokens = AuthStore.RefreshTokens,
                ConsentRecords = AuthStore.ConsentRecords  
            };

            return Results.Json(info, options);
        });
    }
}