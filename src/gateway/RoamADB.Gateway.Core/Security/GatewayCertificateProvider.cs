using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RoamADB.Gateway.Security;

public static class GatewayCertificateProvider
{
  private const string CertificateSubject = "CN=RoamADB Gateway";
  private const string MarkerOid = "1.3.6.1.4.1.61117.1.1";

  public static X509Certificate2 GetOrCreateCurrentUserCertificate()
  {
    if (!OperatingSystem.IsWindows())
    {
      throw new PlatformNotSupportedException("Persistent Gateway identity currently requires Windows.");
    }

    using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
    store.Open(OpenFlags.ReadWrite);

    var existing = store.Certificates
      .Find(X509FindType.FindBySubjectDistinguishedName, CertificateSubject, false)
      .OfType<X509Certificate2>()
      .Where(certificate => certificate.HasPrivateKey
        && certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30)
        && certificate.Extensions.OfType<X509Extension>().Any(extension => extension.Oid?.Value == MarkerOid))
      .OrderByDescending(certificate => certificate.NotAfter)
      .FirstOrDefault();

    if (existing is not null)
    {
      return existing;
    }

    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest(CertificateSubject, key, HashAlgorithmName.SHA256);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

    var writer = new AsnWriter(AsnEncodingRules.DER);
    writer.WriteCharacterString(UniversalTagNumber.UTF8String, "RoamADB Gateway Identity");
    request.CertificateExtensions.Add(new X509Extension(MarkerOid, writer.Encode(), false));

    using var created = request.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddMinutes(-5),
      DateTimeOffset.UtcNow.AddYears(5));
    var temporaryPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    var pkcs12 = created.Export(X509ContentType.Pkcs12, temporaryPassword);
    try
    {
      using var persisted = X509CertificateLoader.LoadPkcs12(
        pkcs12,
        temporaryPassword,
        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
      store.Add(persisted);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(pkcs12);
    }

    var stored = store.Certificates
      .Find(X509FindType.FindByThumbprint, created.Thumbprint, false)
      .OfType<X509Certificate2>()
      .FirstOrDefault()
      ?? throw new CryptographicException("The Gateway certificate could not be reopened after creation.");

    return stored;
  }

  public static X509Certificate2 CreateEphemeralCertificate()
  {
    var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var request = new CertificateRequest(CertificateSubject, key, HashAlgorithmName.SHA256);
    request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
    return request.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddMinutes(-5),
      DateTimeOffset.UtcNow.AddHours(1));
  }

  public static string GetSha256Fingerprint(X509Certificate2 certificate) =>
    Convert.ToHexString(SHA256.HashData(certificate.RawData));
}
