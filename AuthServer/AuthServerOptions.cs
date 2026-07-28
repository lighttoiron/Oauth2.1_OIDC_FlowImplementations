// Server configuration options, these are set in appsettings.json under "AuthServer"
public class AuthServerOptions
{
    public string Issuer { get; set; } = "";
    public string ApiAudience { get; set; } = "";
}