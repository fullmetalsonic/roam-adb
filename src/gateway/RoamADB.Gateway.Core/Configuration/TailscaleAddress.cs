using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace RoamADB.Gateway.Configuration;

public static class TailscaleAddress
{
  private static readonly byte[] TailnetIpv6Prefix = [0xFD, 0x7A, 0x11, 0x5C, 0xA1, 0xE0];

  public static bool IsTailnetAddress(IPAddress address)
  {
    if (address.AddressFamily == AddressFamily.InterNetwork)
    {
      var bytes = address.GetAddressBytes();
      return bytes[0] == 100 && bytes[1] is >= 64 and <= 127;
    }

    if (address.AddressFamily == AddressFamily.InterNetworkV6)
    {
      return address.GetAddressBytes().AsSpan(0, TailnetIpv6Prefix.Length)
        .SequenceEqual(TailnetIpv6Prefix);
    }

    return false;
  }

  public static async Task<IPAddress> ResolveCurrentIpv4Async(
    CancellationToken cancellationToken = default)
  {
    var executable = FindExecutable()
      ?? throw new InvalidOperationException(
        "Tailscale CLI was not found. Install or repair Tailscale on this PC.");

    var startInfo = new ProcessStartInfo
    {
      FileName = executable,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
      CreateNoWindow = true
    };
    startInfo.ArgumentList.Add("ip");
    startInfo.ArgumentList.Add("-4");

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("Tailscale CLI could not be started.");
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(5));

    try
    {
      var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
      var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
      await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
      var output = (await standardOutput.ConfigureAwait(false)).Trim();
      var error = (await standardError.ConfigureAwait(false)).Trim();

      if (process.ExitCode != 0)
      {
        throw new InvalidOperationException(
          string.IsNullOrWhiteSpace(error)
            ? "Tailscale is not ready or is not connected."
            : $"Tailscale is not ready: {FirstLine(error)}");
      }

      var candidates = output.Split(
        ['\r', '\n', ' ', '\t'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
      if (candidates.Length != 1
        || !IPAddress.TryParse(candidates[0], out var address)
        || address.AddressFamily != AddressFamily.InterNetwork
        || !IsTailnetAddress(address))
      {
        throw new InvalidOperationException(
          "Tailscale did not return one valid tailnet IPv4 address.");
      }

      return address;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryTerminate(process);
      throw new TimeoutException("Tailscale did not return its IPv4 address within five seconds.");
    }
  }

  public static string? FindExecutable()
  {
    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var installedExecutable = Path.Combine(programFiles, "Tailscale", "tailscale.exe");
    return File.Exists(installedExecutable) ? installedExecutable : null;
  }

  private static string FirstLine(string value) =>
    value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
      ?? "unknown error";

  private static void TryTerminate(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
      // The process exited between the state check and termination request.
    }
  }
}
