using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using RoamADB.Gateway.Client;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Diagnostics;
using RoamADB.Gateway.Security;
using RoamADB.Gateway.Server;
using RoamADB.Gateway.Storage;

return await GatewayCli.RunAsync(args);

internal static class GatewayCli
{
  public static async Task<int> RunAsync(string[] args)
  {
    var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
    var paths = GatewayPaths.ForCurrentUser();

    return command switch
    {
      "doctor" => RunDoctor(paths),
      "fingerprint" => ShowFingerprint(),
      "status" => await ShowStatusAsync(args),
      "run" => await RunServerAsync(paths, args, openRegistration: args.Contains("--open-registration")),
      "register" => await RunServerAsync(paths, args, openRegistration: true),
      "help" or "--help" or "-h" => ShowHelp(),
      _ => UnknownCommand(command)
    };
  }

  private static int RunDoctor(GatewayPaths paths)
  {
    var checks = GatewayDoctor.Run(paths, GatewayOptions.DefaultPort);
    foreach (var check in checks)
    {
      Console.WriteLine($"{(check.Passed ? "PASS" : check.Required ? "FAIL" : "INFO"),-4} {check.Name,-16} {check.Detail}");
    }

    return checks.Any(check => check.Required && !check.Passed) ? 1 : 0;
  }

  private static int ShowFingerprint()
  {
    using var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
    Console.WriteLine(FormatFingerprint(GatewayCertificateProvider.GetSha256Fingerprint(certificate)));
    return 0;
  }

  private static async Task<int> ShowStatusAsync(string[] args)
  {
    var port = ReadPort(args);
    GatewayEndpoint endpoint;
    try
    {
      endpoint = await ResolveEndpointAsync(args);
    }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
    {
      Console.Error.WriteLine(exception.Message);
      return 1;
    }

    using var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
    var fingerprint = GatewayCertificateProvider.GetSha256Fingerprint(certificate);
    var client = new GatewayProbeClient(endpoint.Address.ToString(), port, fingerprint);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    try
    {
      var response = await client.PingAsync(timeout.Token);
      Console.WriteLine(response.Type == "pong" ? "RoamADB Gateway is running." : "Unexpected Gateway response.");
      return response.Type == "pong" ? 0 : 1;
    }
    catch (Exception exception) when (exception is SocketException or IOException or OperationCanceledException)
    {
      Console.Error.WriteLine(
        endpoint.Exposure == GatewayExposure.TailnetOnly
          ? "RoamADB Gateway is not reachable on this PC's Tailscale address."
          : "RoamADB Gateway is not reachable on loopback.");
      return 1;
    }
  }

  private static async Task<int> RunServerAsync(
    GatewayPaths paths,
    string[] args,
    bool openRegistration)
  {
    var port = ReadPort(args);
    GatewayEndpoint endpoint;
    try
    {
      endpoint = await ResolveEndpointAsync(args);
    }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
    {
      Console.Error.WriteLine(exception.Message);
      return 1;
    }

    var options = new GatewayOptions
    {
      ListenAddress = endpoint.Address,
      Exposure = endpoint.Exposure,
      Port = port
    };
    var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
    var registry = new FileDeviceRegistry(paths.DeviceRegistryPath);
    var registrationCodes = new RegistrationCodeManager();
    await using var server = new GatewayServer(options, certificate, registry, registrationCodes);
    server.RelayPublished += relay =>
    {
      var adbCommand = relay.RelayKind == "pairing" ? "adb pair" : "adb connect";
      Console.WriteLine(
        $"{relay.RelayKind} relay ready for {relay.DeviceId}: {adbCommand} 127.0.0.1:{relay.LocalPort}");
    };

    if (openRegistration)
    {
      var ticket = registrationCodes.Issue();
      Console.WriteLine($"Registration code: {ticket.Code}");
      Console.WriteLine($"Expires (UTC): {ticket.ExpiresAt:O}");
    }

    Console.WriteLine($"Gateway fingerprint: {FormatFingerprint(server.Fingerprint)}");
    Console.WriteLine(
      endpoint.Exposure == GatewayExposure.TailnetOnly
        ? $"Listening on {endpoint.Address}:{port} (exact Tailscale interface only)"
        : $"Listening on 127.0.0.1:{port} (loopback only)");
    if (endpoint.Exposure == GatewayExposure.TailnetOnly)
    {
      Console.WriteLine("Router port forwarding is not required. Windows Firewall is not changed automatically.");
    }
    Console.WriteLine($"ADB connect relay: 127.0.0.1:{options.AdbConnectRelayPort} (opened only after phone authentication)");
    Console.WriteLine($"ADB pairing relay: 127.0.0.1:{options.AdbPairingRelayPort} (opened only on pairing request)");
    Console.WriteLine("Press Ctrl+C to stop.");

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
      eventArgs.Cancel = true;
      cancellation.Cancel();
    };

    await server.RunAsync(cancellation.Token);
    return 0;
  }

  private static int ReadPort(string[] args)
  {
    var index = Array.IndexOf(args, "--port");
    if (index < 0)
    {
      return GatewayOptions.DefaultPort;
    }

    if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out var port) || port is < 1 or > 65535)
    {
      throw new ArgumentException("--port requires a TCP port from 1 to 65535.");
    }

    return port;
  }

  private static async Task<GatewayEndpoint> ResolveEndpointAsync(string[] args)
  {
    if (!args.Contains("--tailnet", StringComparer.OrdinalIgnoreCase))
    {
      return new GatewayEndpoint(IPAddress.Loopback, GatewayExposure.LoopbackOnly);
    }

    var address = await TailscaleAddress.ResolveCurrentIpv4Async();
    return new GatewayEndpoint(address, GatewayExposure.TailnetOnly);
  }

  private static string FormatFingerprint(string fingerprint) =>
    string.Join(':', Enumerable.Range(0, fingerprint.Length / 2).Select(index => fingerprint.Substring(index * 2, 2)));

  private static int ShowHelp()
  {
    Console.WriteLine("RoamADB Gateway technical spike");
    Console.WriteLine();
    Console.WriteLine("  doctor                       Check local prerequisites");
    Console.WriteLine("  fingerprint                  Show the Gateway TLS fingerprint");
    Console.WriteLine("  run [--port N] [--tailnet]      Start loopback or exact-tailnet Gateway");
    Console.WriteLine("  register [--port N] [--tailnet] Start with a two-minute registration code");
    Console.WriteLine("  status [--port N] [--tailnet]   Probe loopback or this PC's tailnet address");
    return 0;
  }

  private static int UnknownCommand(string command)
  {
    Console.Error.WriteLine($"Unknown command: {command}");
    return 2;
  }

  private sealed record GatewayEndpoint(IPAddress Address, GatewayExposure Exposure);
}
