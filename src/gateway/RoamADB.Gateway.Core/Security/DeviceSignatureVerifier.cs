using System.Security.Cryptography;

namespace RoamADB.Gateway.Security;

public static class DeviceSignatureVerifier
{
  public static void ValidatePublicKey(string publicKeySpkiBase64)
  {
    var bytes = Convert.FromBase64String(publicKeySpkiBase64);
    using var key = ECDsa.Create();
    key.ImportSubjectPublicKeyInfo(bytes, out var consumed);
    if (consumed != bytes.Length || key.KeySize != 256)
    {
      throw new CryptographicException("Only complete ECDSA P-256 public keys are accepted.");
    }
  }

  public static bool Verify(string publicKeySpkiBase64, byte[] challenge, string signatureBase64)
  {
    try
    {
      var publicKey = Convert.FromBase64String(publicKeySpkiBase64);
      var signature = Convert.FromBase64String(signatureBase64);
      using var key = ECDsa.Create();
      key.ImportSubjectPublicKeyInfo(publicKey, out var consumed);
      return consumed == publicKey.Length
        && key.KeySize == 256
        && key.VerifyData(challenge, signature, HashAlgorithmName.SHA256);
    }
    catch (FormatException)
    {
      return false;
    }
    catch (CryptographicException)
    {
      return false;
    }
  }
}
