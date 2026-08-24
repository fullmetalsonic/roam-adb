using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Security;
using RoamADB.Gateway.Storage;

namespace RoamADB.Gateway.Diagnostics;

public sealed record DoctorCheck(string Name, bool Passed, string Detail, bool Required = true);

public static class GatewayDoctor
{
  public static IReadOnlyList<DoctorCheck> Run(GatewayPaths paths, int port)
  {
    var checks = new List<DoctorCheck>
    {
      new("windows", OperatingSystem.IsWindows(), "RoamADB Gateway currently targets Windows 11."),
      CheckStorage(paths),
      CheckCertificate(),
      CheckPort(port),
      CheckAdb(),
      CheckTailscale()
    };
    return checks;
  }

  private static DoctorCheck CheckStorage(GatewayPaths paths)
  {
    try
    {
      Directory.CreateDirectory(paths.RootDirectory);
      var probe = Path.Combine(paths.RootDirectory, $"write-probe-{Guid.NewGuid():N}.tmp");
      File.WriteAllText(probe, "RoamADB");
      File.Delete(probe);
      return new DoctorCheck("storage", true, "Current-user Gateway storage is writable.");
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      return new DoctorCheck("storage", false, "Gateway storage is not writable.");
    }
  }

  private static DoctorCheck CheckCertificate()
  {
    try
    {
      using var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
      return new DoctorCheck(
        "identity",
        certificate.HasPrivateKey,
        certificate.HasPrivateKey
          ? "Gateway identity is available in the current-user certificate store."
          : "Gateway identity has no private key.");
    }
    catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
    {
      return new DoctorCheck("identity", false, "Gateway identity could not be created or loaded.");
    }
  }

  private static DoctorCheck CheckPort(int port)
  {
    try
    {
      var listener = new TcpListener(IPAddress.Loopback, port);
      listener.Start();
      listener.Stop();
      return new DoctorCheck("loopback_port", true, $"Loopback TCP {port} is available.");
    }
    catch (SocketException)
    {
      return new DoctorCheck("loopback_port", false, $"Loopback TCP {port} is already in use.");
    }
  }

  private static DoctorCheck CheckAdb()
  {
    var candidates = new[]
    {
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Android", "Sdk", "platform-tools", "adb.exe"),
      Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe")
    };
    var found = candidates.FirstOrDefault(File.Exists);
    return new DoctorCheck(
      "adb",
      found is not null,
      found is null ? "ADB was not found in known locations." : "ADB was found in a known local location.",
      Required: false);
  }

  private static DoctorCheck CheckTailscale()
  {
    var found = TailscaleAddress.FindExecutable() is not null;
    return new DoctorCheck(
      "tailscale",
      found,
      found
        ? "Tailscale CLI is installed; run or register with --tailnet to use it."
        : "Tailscale CLI was not found.",
      Required: false);
  }
}
