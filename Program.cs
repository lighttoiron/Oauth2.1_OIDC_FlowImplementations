using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add Http logging so we can see the requests and responses in the console
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = Microsoft.AspNetCore.HttpLogging.HttpLoggingFields.All;
});
//

var app = builder.Build();

// Generate public and private keys (RSA key pair)
// In production this would be loaded from secure storage, not generated here (Azure Key Vault, AWS KMS, etc.)
// that way the private key is never exposed in source or config files and survives across restarts, shared across instances, etc.
string rsaKeyId = "lab-key-1"; // In production this would be a stable value that does not change across restarts or deployments.  Could be a GUID, a hash of the public key, or any other unique identifier.
var rsa = RSA.Create(2048); // 2048 is web standard length for RSA keys in bits.  1024 is not secure enough, 4096 is overkill and slower, but maybe for super security ok?
// Note: The above RSA object holds both the private and public key parameters.
//  To get the private key, we need to wrap those params with the RsaSecurityKey class, passing our keyId
//  To get the public key, we need to export only the public parameters, then wrap those with the RsaSecurityKey class, passing the same keyId.
var privateKey = new RsaSecurityKey(rsa) { KeyId = rsaKeyId }; // KeyId is used to identify the key in the JWKS endpoint.  This is important for clients to know which key to use to verify the signature of the JWTs issued by this auth server.
var publicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false))
{
    KeyId = rsaKeyId
};
//

// Set up pretty print JSON capabilities for easy reading when testing
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true
};
//

// Add the Http logging into the middleware pipeline
app.UseHttpLogging();
//

const string Issuer = "https://localhost:7051";

// Add the jwks (JSON Web Key Set) endpoint, which provides public keys to the client so they can verify the signatures of the JWTs issued by this auth server.
JWKSEndpoint.Setup(app, publicKey, jsonOptions);
//

// Add the Discovery endpoint, which provides info about the auth server to the clients.
DiscoveryEndpoint.Setup(app, Issuer, jsonOptions);
//

// Add the /authorization endpoint
//  https://localhost:7051/authorize?response_type=code&client_id=test-client&redirect_uri=https://localhost:3000/callback&scope=openid&state=abc123&code_challenge=abc&code_challenge_method=S256
AuthorizeEndpoint.Setup(app, Issuer);
//


app.Run();