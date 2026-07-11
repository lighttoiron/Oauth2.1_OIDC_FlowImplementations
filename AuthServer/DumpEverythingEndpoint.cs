using System.Text.Json;

static class DumpEverythingEndpoint
{
    public static void Map(WebApplication app, JsonSerializerOptions options)
    {
        app.MapGet("/dumpeverything", (HttpContext context) =>
        {
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