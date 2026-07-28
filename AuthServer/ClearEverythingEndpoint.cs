// A lab-specific endpoint used for clearing all stored information and cookies.
// Not a part of OAuth or OIDC
static class ClearEverythingEndpoint
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/cleareverything", (HttpContext context) =>
        {
            // Clear everything in our stores (except valid username/password combos)
            AuthStore.ActiveSessions.Clear();
            AuthStore.AuthCodes.Clear();
            AuthStore.ConsentRecords.Clear();
            AuthStore.PendingConsentRequests.Clear();
            AuthStore.PendingRequests.Clear();
            AuthStore.RefreshTokens.Clear();

            // NOTE: Because we are never navigating to this page and are only calling this endpoint through the BFF backend,
            // this cookie setting will never delete the cookie from our page.  Technically, the user should be redirected to this page in a full page
            // redirect or popup, but logout is currently outside the scope of this project.
            // This cookie is cleared, however, by the /authorize endpoint so it should still clean up the next time the user attempts to log in.
            context.Response.Cookies.Delete(AuthorizeEndpoint.AuthenticatedSessionCookieName);

            return Results.Ok;
        });
    }
}