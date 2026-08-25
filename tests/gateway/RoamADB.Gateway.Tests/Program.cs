using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using RoamADB.Gateway.Client;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Protocol;
using RoamADB.Gateway.Registration;
using RoamADB.Gateway.Security;
using RoamADB.Gateway.Server;

var tests = new (string Name, Func<Task> Run)[]
{
  ("registration code is one-time", RegistrationCodeIsOneTimeAsync),
  ("registration rejects reuse", RegistrationRejectsReuseAsync),
  ("registration QR uses bounded safe fields", RegistrationQrUsesBoundedSafeFieldsAsync),
  ("non-loopback listener is rejected", NonLoopbackListenerIsRejectedAsync),
  ("tailnet listener accepts exact reserved address", TailnetListenerAcceptsReservedAddressAsync),
  ("tailnet listener rejects public and wildcard addresses", TailnetListenerRejectsUnsafeAddressesAsync),
  ("signature verifier accepts matching key", SignatureVerifierAcceptsMatchingKeyAsync),
  ("signature verifier rejects another key", SignatureVerifierRejectsAnotherKeyAsync),
  ("file registry round trip", FileRegistryRoundTripAsync),
  ("TLS fingerprint pin rejects mismatch", FingerprintPinRejectsMismatchAsync),
  ("register and authenticate integration", RegisterAndAuthenticateIntegrationAsync),
  ("authenticated session outlives login deadline", AuthenticatedSessionOutlivesLoginDeadlineAsync),
  ("authenticated relay forwards binary traffic", AuthenticatedRelayForwardsBinaryTrafficAsync),
  ("relay start deadline closes stalled local client", RelayStartDeadlineClosesStalledLocalClientAsync),
  ("phone close releases unpublished relay port", PhoneCloseReleasesUnpublishedRelayPortAsync)
};

var failures = 0;
foreach (var test in tests)
{
  try
  {
    await test.Run();
    Console.WriteLine($"PASS {test.Name}");
  }
  catch (Exception exception)
  {
    failures++;
    Console.WriteLine($"FAIL {test.Name}: {exception.Message}");
  }
}

Console.WriteLine($"RESULT {tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static Task RegistrationCodeIsOneTimeAsync()
{
  var manager = new RegistrationCodeManager();
  var ticket = manager.Issue();
  Assert.True(manager.TryConsume(ticket.Code), "The issued code was rejected.");
  Assert.False(manager.TryConsume(ticket.Code), "A consumed code was accepted again.");
  return Task.CompletedTask;
}

static Task RegistrationRejectsReuseAsync()
{
  var manager = new RegistrationCodeManager(maximumAttempts: 2);
  var ticket = manager.Issue();
  Assert.False(manager.TryConsume("999999"), "An invalid code was accepted.");
  Assert.False(manager.TryConsume("888888"), "A second invalid code was accepted.");
  Assert.False(manager.TryConsume(ticket.Code), "A code survived the maximum failed attempts.");
  return Task.CompletedTask;
}

static Task RegistrationQrUsesBoundedSafeFieldsAsync()
{
  var ticket = new RegistrationTicket("123456", DateTimeOffset.FromUnixTimeSeconds(2_000));
  var payload = new RegistrationPayload(
    IPAddress.Parse("100.95.12.3"),
    GatewayOptions.DefaultPort,
    new string('A', 64),
    ticket);
  var uri = payload.ToUri();
  Assert.True(uri.StartsWith("roamadb://register?", StringComparison.Ordinal), "Unexpected QR URI scheme.");
  Assert.True(uri.Contains("host=100.95.12.3", StringComparison.Ordinal), "QR URI lost the Gateway host.");
  Assert.True(uri.Contains("code=123456", StringComparison.Ordinal), "QR URI lost the one-time code.");
  Assert.False(uri.Contains("private", StringComparison.OrdinalIgnoreCase), "QR URI exposed a private-key field.");
  Assert.Throws<InvalidOperationException>(
    () => new RegistrationPayload(
      IPAddress.Parse("100.95.12.3"),
      GatewayOptions.DefaultPort,
      "not-a-fingerprint",
      ticket).ToUri(),
    "An invalid fingerprint was encoded into the QR URI.");
  return Task.CompletedTask;
}

static Task NonLoopbackListenerIsRejectedAsync()
{
  var options = new GatewayOptions { ListenAddress = IPAddress.Any };
  Assert.Throws<InvalidOperationException>(
    options.Validate,
    "A non-loopback listener passed the technical-spike safety gate.");
  return Task.CompletedTask;
}

static Task TailnetListenerAcceptsReservedAddressAsync()
{
  new GatewayOptions
  {
    ListenAddress = IPAddress.Parse("100.64.0.1"),
    Exposure = GatewayExposure.TailnetOnly
  }.Validate();
  new GatewayOptions
  {
    ListenAddress = IPAddress.Parse("100.127.255.254"),
    Exposure = GatewayExposure.TailnetOnly
  }.Validate();
  new GatewayOptions
  {
    ListenAddress = IPAddress.Parse("fd7a:115c:a1e0::1"),
    Exposure = GatewayExposure.TailnetOnly
  }.Validate();
  return Task.CompletedTask;
}

static Task TailnetListenerRejectsUnsafeAddressesAsync()
{
  foreach (var address in new[]
  {
    IPAddress.Parse("100.63.255.255"),
    IPAddress.Parse("100.128.0.1"),
    IPAddress.Parse("192.168.1.10"),
    IPAddress.Parse("8.8.8.8"),
    IPAddress.Any,
    IPAddress.IPv6Any
  })
  {
    var options = new GatewayOptions
    {
      ListenAddress = address,
      Exposure = GatewayExposure.TailnetOnly
    };
    Assert.Throws<InvalidOperationException>(
      options.Validate,
      $"Unsafe tailnet listener address {address} passed validation.");
  }

  Assert.Throws<InvalidOperationException>(
    new GatewayOptions { Exposure = (GatewayExposure)999 }.Validate,
    "An unknown Gateway exposure policy passed validation.");

  return Task.CompletedTask;
}

static Task SignatureVerifierAcceptsMatchingKeyAsync()
{
  using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var challenge = RandomNumberGenerator.GetBytes(32);
  var signature = key.SignData(
    challenge,
    HashAlgorithmName.SHA256,
    DSASignatureFormat.Rfc3279DerSequence);
  Assert.True(signature.Length > 0 && signature[0] == 0x30, "The regression signature is not DER encoded.");
  var publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
  Assert.True(
    DeviceSignatureVerifier.Verify(publicKey, challenge, Convert.ToBase64String(signature)),
    "A valid signature was rejected.");
  return Task.CompletedTask;
}

static Task SignatureVerifierRejectsAnotherKeyAsync()
{
  using var enrolledKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var challenge = RandomNumberGenerator.GetBytes(32);
  var signature = attackerKey.SignData(
    challenge,
    HashAlgorithmName.SHA256,
    DSASignatureFormat.Rfc3279DerSequence);
  var publicKey = Convert.ToBase64String(enrolledKey.ExportSubjectPublicKeyInfo());
  Assert.False(
    DeviceSignatureVerifier.Verify(publicKey, challenge, Convert.ToBase64String(signature)),
    "A signature from another key was accepted.");
  return Task.CompletedTask;
}

static async Task FileRegistryRoundTripAsync()
{
  var root = Path.Combine(Path.GetTempPath(), "roamadb-tests", Guid.NewGuid().ToString("N"));
  try
  {
    var registry = new FileDeviceRegistry(Path.Combine(root, "devices.json"));
    var device = new DeviceRecord("fold8", "Galaxy Z Fold8", "public-key", DateTimeOffset.UtcNow);
    await registry.UpsertAsync(device, CancellationToken.None);
    var loaded = await registry.FindAsync("fold8", CancellationToken.None);
    Assert.Equal(device, loaded, "The persisted device record changed.");
    Assert.True(await registry.RemoveAsync("fold8", CancellationToken.None), "The device was not removed.");
    Assert.True(await registry.FindAsync("fold8", CancellationToken.None) is null, "The removed device remained.");
  }
  finally
  {
    if (Directory.Exists(root))
    {
      Directory.Delete(root, true);
    }
  }
}

static async Task FingerprintPinRejectsMismatchAsync()
{
  await using var fixture = await GatewayFixture.StartAsync();
  var client = new GatewayProbeClient("127.0.0.1", fixture.Server.BoundPort, new string('0', 64));
  await Assert.ThrowsAsync<AuthenticationException>(
    () => client.PingAsync(CancellationToken.None),
    "A mismatched TLS fingerprint was accepted.");
}

static async Task RegisterAndAuthenticateIntegrationAsync()
{
  await using var fixture = await GatewayFixture.StartAsync();
  using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var ticket = fixture.RegistrationCodes.Issue();
  var client = new GatewayProbeClient(
    "127.0.0.1",
    fixture.Server.BoundPort,
    fixture.Server.Fingerprint);

  var registration = await client.RegisterAsync(
    new RegistrationRequest(
      "fold8-test",
      "Galaxy Z Fold8 Test",
      Convert.ToBase64String(phoneKey.ExportSubjectPublicKeyInfo()),
      ticket.Code),
    CancellationToken.None);
  Assert.Equal("registered", registration.Type, "Registration failed.");

  var authentication = await client.AuthenticateAsync("fold8-test", phoneKey, CancellationToken.None);
  Assert.Equal("authenticated", authentication.Type, "Authentication failed.");

  var attackerAuthentication = await client.AuthenticateAsync(
    "fold8-test",
    ECDsa.Create(ECCurve.NamedCurves.nistP256),
    CancellationToken.None);
  Assert.Equal("error", attackerAuthentication.Type, "Another private key authenticated.");
}

static async Task AuthenticatedSessionOutlivesLoginDeadlineAsync()
{
  await using var fixture = await GatewayFixture.StartAsync(TimeSpan.FromMilliseconds(500));
  using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var ticket = fixture.RegistrationCodes.Issue();
  var client = new GatewayProbeClient(
    "127.0.0.1",
    fixture.Server.BoundPort,
    fixture.Server.Fingerprint);

  var registration = await client.RegisterAsync(
    new RegistrationRequest(
      "fold8-session-test",
      "Galaxy Z Fold8 Session Test",
      Convert.ToBase64String(phoneKey.ExportSubjectPublicKeyInfo()),
      ticket.Code),
    CancellationToken.None);
  Assert.Equal("registered", registration.Type, "Registration failed.");

  await using var session = await client.OpenAuthenticatedSessionAsync(
    "fold8-session-test",
    phoneKey,
    CancellationToken.None);
  await Task.Delay(750);
  var pong = await session.PingAsync(CancellationToken.None);
  Assert.Equal("pong", pong.Type, "The established session was cut off by the login deadline.");
}

static async Task AuthenticatedRelayForwardsBinaryTrafficAsync()
{
  await using var fixture = await GatewayFixture.StartAsync();
  using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var ticket = fixture.RegistrationCodes.Issue();
  var client = new GatewayProbeClient(
    "127.0.0.1",
    fixture.Server.BoundPort,
    fixture.Server.Fingerprint);

  var registration = await client.RegisterAsync(
    new RegistrationRequest(
      "fold8-relay-test",
      "Galaxy Z Fold8 Relay Test",
      Convert.ToBase64String(phoneKey.ExportSubjectPublicKeyInfo()),
      ticket.Code),
    CancellationToken.None);
  Assert.Equal("registered", registration.Type, "Relay test registration failed.");

  await using var phoneSession = await client.OpenAuthenticatedSessionAsync(
    "fold8-relay-test",
    phoneKey,
    CancellationToken.None);
  var published = await phoneSession.PublishRelayAsync("connect", CancellationToken.None);
  Assert.Equal("relay_published", published.Type, "The authenticated relay was not published.");
  Assert.True(published.RelayPort is > 0, "The Gateway did not return a loopback relay port.");

  using var localAdbClient = new TcpClient();
  await localAdbClient.ConnectAsync(
    IPAddress.Loopback,
    published.RelayPort!.Value,
    CancellationToken.None);
  var started = await phoneSession.AcceptRelayAsync(CancellationToken.None);
  Assert.Equal("relay_start", started.Type, "The phone was not asked to start its local ADB socket.");

  var pcPayload = new byte[] { 0x00, 0x41, 0x44, 0x42, 0xFF, 0x0A };
  await localAdbClient.GetStream().WriteAsync(pcPayload, CancellationToken.None);
  var receivedByPhone = await ReadExactlyFromRelayAsync(phoneSession, pcPayload.Length);
  Assert.SequenceEqual(pcPayload, receivedByPhone, "PC-to-phone relay bytes changed.");

  var phonePayload = new byte[] { 0x54, 0x4C, 0x53, 0x00, 0xFE, 0x7F };
  await phoneSession.WriteRawAsync(phonePayload, CancellationToken.None);
  var receivedByPc = new byte[phonePayload.Length];
  await localAdbClient.GetStream().ReadExactlyAsync(receivedByPc, CancellationToken.None);
  Assert.SequenceEqual(phonePayload, receivedByPc, "Phone-to-PC relay bytes changed.");
}

static async Task<byte[]> ReadExactlyFromRelayAsync(GatewayProbeSession session, int length)
{
  var buffer = new byte[length];
  var offset = 0;
  while (offset < buffer.Length)
  {
    var read = await session.ReadRawAsync(buffer.AsMemory(offset), CancellationToken.None);
    if (read == 0)
    {
      throw new EndOfStreamException("The relay closed before the expected payload arrived.");
    }

    offset += read;
  }

  return buffer;
}

static async Task RelayStartDeadlineClosesStalledLocalClientAsync()
{
  await using var fixture = await GatewayFixture.StartAsync(TimeSpan.FromMilliseconds(300));
  using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var ticket = fixture.RegistrationCodes.Issue();
  var client = new GatewayProbeClient(
    "127.0.0.1",
    fixture.Server.BoundPort,
    fixture.Server.Fingerprint);

  await client.RegisterAsync(
    new RegistrationRequest(
      "fold8-stalled-relay-test",
      "Galaxy Z Fold8 Stalled Relay Test",
      Convert.ToBase64String(phoneKey.ExportSubjectPublicKeyInfo()),
      ticket.Code),
    CancellationToken.None);
  await using var phoneSession = await client.OpenAuthenticatedSessionAsync(
    "fold8-stalled-relay-test",
    phoneKey,
    CancellationToken.None);
  var published = await phoneSession.PublishRelayAsync("connect", CancellationToken.None);

  using var localAdbClient = new TcpClient();
  await localAdbClient.ConnectAsync(
    IPAddress.Loopback,
    published.RelayPort!.Value,
    CancellationToken.None);
  using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
  var read = await localAdbClient.GetStream().ReadAsync(new byte[1], readTimeout.Token);
  Assert.Equal(0, read, "A phone that never returned relay_ready held the local ADB socket open.");
}

static async Task PhoneCloseReleasesUnpublishedRelayPortAsync()
{
  var relayPort = ReserveLoopbackPort();
  await using var fixture = await GatewayFixture.StartAsync(adbConnectRelayPort: relayPort);
  using var phoneKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var ticket = fixture.RegistrationCodes.Issue();
  var client = new GatewayProbeClient(
    "127.0.0.1",
    fixture.Server.BoundPort,
    fixture.Server.Fingerprint);

  await client.RegisterAsync(
    new RegistrationRequest(
      "fold8-relay-republish-test",
      "Galaxy Z Fold8 Relay Republish Test",
      Convert.ToBase64String(phoneKey.ExportSubjectPublicKeyInfo()),
      ticket.Code),
    CancellationToken.None);

  await using (var firstSession = await client.OpenAuthenticatedSessionAsync(
    "fold8-relay-republish-test",
    phoneKey,
    CancellationToken.None))
  {
    var firstPublication = await firstSession.PublishRelayAsync("connect", CancellationToken.None);
    Assert.Equal("relay_published", firstPublication.Type, "The first relay was not published.");
    Assert.Equal(relayPort, firstPublication.RelayPort, "The first relay used an unexpected port.");
  }

  using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
  await using var secondSession = await client.OpenAuthenticatedSessionAsync(
    "fold8-relay-republish-test",
    phoneKey,
    deadline.Token);
  var secondPublication = await secondSession.PublishRelayAsync("connect", deadline.Token);
  Assert.Equal(
    "relay_published",
    secondPublication.Type,
    "The phone close left the unpublished relay listener occupied.");
  Assert.Equal(relayPort, secondPublication.RelayPort, "The republished relay used an unexpected port.");
}

static int ReserveLoopbackPort()
{
  var listener = new TcpListener(IPAddress.Loopback, 0);
  listener.Start();
  try
  {
    return ((IPEndPoint)listener.LocalEndpoint).Port;
  }
  finally
  {
    listener.Stop();
  }
}

file sealed class GatewayFixture : IAsyncDisposable
{
  private readonly CancellationTokenSource _cancellation;
  private readonly Task _serverTask;

  private GatewayFixture(
    GatewayServer server,
    RegistrationCodeManager registrationCodes,
    CancellationTokenSource cancellation,
    Task serverTask)
  {
    Server = server;
    RegistrationCodes = registrationCodes;
    _cancellation = cancellation;
    _serverTask = serverTask;
  }

  public GatewayServer Server { get; }
  public RegistrationCodeManager RegistrationCodes { get; }

  public static async Task<GatewayFixture> StartAsync(
    TimeSpan? authenticationTimeout = null,
    int? adbConnectRelayPort = null)
  {
    var options = new GatewayOptions
    {
      ListenAddress = IPAddress.Loopback,
      Port = 0,
      AdbConnectRelayPort = adbConnectRelayPort ?? 0,
      AdbPairingRelayPort = 0,
      AuthenticationTimeout = authenticationTimeout ?? TimeSpan.FromSeconds(15)
    };
    var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
    var registrationCodes = new RegistrationCodeManager();
    var server = new GatewayServer(options, certificate, new InMemoryDeviceRegistry(), registrationCodes);
    server.ClientFault += exception =>
      Console.WriteLine($"SERVER-DIAGNOSTIC {exception.GetType().Name}: {exception.Message}");
    var cancellation = new CancellationTokenSource();
    var task = server.RunAsync(cancellation.Token);

    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (server.BoundPort == 0 && DateTime.UtcNow < deadline)
    {
      await Task.Delay(10);
    }

    if (server.BoundPort == 0)
    {
      cancellation.Cancel();
      throw new TimeoutException("The test Gateway did not start.");
    }

    return new GatewayFixture(server, registrationCodes, cancellation, task);
  }

  public async ValueTask DisposeAsync()
  {
    await _cancellation.CancelAsync();
    await _serverTask;
    await Server.DisposeAsync();
    _cancellation.Dispose();
  }
}

file static class Assert
{
  public static void True(bool condition, string message)
  {
    if (!condition)
    {
      throw new InvalidOperationException(message);
    }
  }

  public static void False(bool condition, string message) => True(!condition, message);

  public static void Equal<T>(T expected, T? actual, string message)
  {
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
      throw new InvalidOperationException($"{message} Expected '{expected}', got '{actual}'.");
    }
  }

  public static void SequenceEqual(
    ReadOnlySpan<byte> expected,
    ReadOnlySpan<byte> actual,
    string message)
  {
    if (!expected.SequenceEqual(actual))
    {
      throw new InvalidOperationException(message);
    }
  }

  public static async Task ThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
  {
    try
    {
      await action();
    }
    catch (TException)
    {
      return;
    }

    throw new InvalidOperationException(message);
  }

  public static void Throws<TException>(Action action, string message)
    where TException : Exception
  {
    try
    {
      action();
    }
    catch (TException)
    {
      return;
    }

    throw new InvalidOperationException(message);
  }
}
