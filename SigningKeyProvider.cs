using System;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

public interface ISigningKeyProvider
{
    RsaSecurityKey PrivateKey { get; }
    RsaSecurityKey PublicKey { get; }
}

// We are creating our own signing key provider class for this application
// These values should not typically be generated in the server exposing the keys,
//  they should be stored in a secure database with separate configuration, like Azure Key Vault or AWS KMS
// Private key bytes should never be held in our backend server directly
public class InMemorySigningKeyProvider : ISigningKeyProvider
{
    public RsaSecurityKey PrivateKey { get; }
    public RsaSecurityKey PublicKey { get; }

    public InMemorySigningKeyProvider()
    {
        // Generate public and private keys (RSA key pair)
        // In production this would be loaded from secure storage, not generated here (Azure Key Vault, AWS KMS, etc.)
        // that way the private key is never exposed in source or config files and survives across restarts, shared across instances, etc.
        string rsaKeyId = "lab-key-1"; // In production this would be a stable value that does not change across restarts or deployments.  Could be a GUID, a hash of the public key, or any other unique identifier.
        var rsa = RSA.Create(2048); // 2048 is web standard length for RSA keys in bits.  1024 is not secure enough, 4096 is overkill and slower, but maybe for super security ok?
        // Note: The above RSA object holds both the private and public key parameters.
        //  To get the private key, we need to wrap those params with the RsaSecurityKey class, passing our keyId
        //  To get the public key, we need to export only the public parameters, then wrap those with the RsaSecurityKey class, passing the same keyId.
        PrivateKey = new RsaSecurityKey(rsa) { KeyId = rsaKeyId }; // KeyId is used to identify the key in the JWKS endpoint.  This is important for clients to know which key to use to verify the signature of the JWTs issued by this auth server.
        PublicKey = new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: false))
        {
            KeyId = rsaKeyId
        };
    }
}