using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Protocol;
using RoamADB.Gateway.Security;

namespace RoamADB.Gateway.Server;

public sealed class GatewayServer(
  GatewayOptions options,
  X509Certificate2 certificate,
  IDeviceRegistry deviceRegistry,
  RegistrationCodeManager registrationCodes) : IAsyncDisposable
{
  private readonly TcpListener _listener = new(options.ListenAddress, options.Port);
  private readonly ConcurrentDictionary<long, Task> _clientTasks = new();
  private readonly SemaphoreSlim _clientSlots = new(options.MaxConcurrentClients, options.MaxConcurrentClients);
  private readonly SemaphoreSlim _connectRelaySlot = new(1, 1);
  private readonly SemaphoreSlim _pairingRelaySlot = new(1, 1);
  private readonly CancellationTokenSource _stop = new();
  private long _clientSequence;
  private bool _started;

  public int BoundPort { get; private set; }
  public string Fingerprint => GatewayCertificateProvider.GetSha256Fingerprint(certificate);
  public event Action<Exception>? ClientFault;
  public event Action<DeviceRecord>? DeviceRegistered;
  public event Action<RelayPublishedEvent>? RelayPublished;

  public async Task RunAsync(CancellationToken cancellationToken)
  {
    options.Validate();
    if (_started)
    {
      throw new InvalidOperationException("The Gateway server has already been started.");
    }

    _started = true;
    using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
      cancellationToken,
      _stop.Token);

    _listener.Start();
    BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

    try
    {
      while (!linkedCancellation.IsCancellationRequested)
      {
        TcpClient client;
        try
        {
          client = await _listener.AcceptTcpClientAsync(linkedCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
          break;
        }

        await _clientSlots.WaitAsync(linkedCancellation.Token).ConfigureAwait(false);
        var id = Interlocked.Increment(ref _clientSequence);
        var clientTask = HandleClientSafelyAsync(client, linkedCancellation.Token);
        _clientTasks[id] = clientTask;
        _ = clientTask.ContinueWith(
          completedTask =>
          {
            _clientTasks.TryRemove(id, out var removedTask);
            _clientSlots.Release();
            _ = completedTask.Exception;
            _ = removedTask;
          },
          CancellationToken.None,
          TaskContinuationOptions.ExecuteSynchronously,
          TaskScheduler.Default);
      }
    }
    finally
    {
      _listener.Stop();
      await Task.WhenAll(_clientTasks.Values).ConfigureAwait(false);
    }
  }

  public async ValueTask DisposeAsync()
  {
    await _stop.CancelAsync().ConfigureAwait(false);
    _listener.Stop();
    certificate.Dispose();
    _clientSlots.Dispose();
    _connectRelaySlot.Dispose();
    _pairingRelaySlot.Dispose();
    _stop.Dispose();
  }

  private async Task HandleClientSafelyAsync(TcpClient client, CancellationToken cancellationToken)
  {
    using (client)
    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
    {
      timeout.CancelAfter(options.AuthenticationTimeout);
      try
      {
        await using var ssl = new SslStream(client.GetStream(), false);
        await ssl.AuthenticateAsServerAsync(
          new SslServerAuthenticationOptions
          {
            ServerCertificate = certificate,
            ClientCertificateRequired = false,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
          },
          timeout.Token).ConfigureAwait(false);

        await ProcessProtocolAsync(client, ssl, timeout.Token, cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) when (timeout.IsCancellationRequested || cancellationToken.IsCancellationRequested)
      {
        // Authentication deadlines fail closed without exposing details to the peer.
      }
      catch (AuthenticationException exception)
      {
        ClientFault?.Invoke(exception);
        // Invalid TLS clients are intentionally ignored.
      }
      catch (IOException exception)
      {
        ClientFault?.Invoke(exception);
        // The peer disconnected or sent an invalid bounded frame.
      }
      catch (SocketException exception)
      {
        ClientFault?.Invoke(exception);
        // A local relay port was unavailable or a socket closed unexpectedly.
      }
      catch (JsonException exception)
      {
        ClientFault?.Invoke(exception);
        // Malformed JSON fails closed.
      }
      catch (CryptographicException exception)
      {
        ClientFault?.Invoke(exception);
        // Invalid key material fails closed.
      }
    }
  }

  private async Task ProcessProtocolAsync(
    TcpClient phoneClient,
    SslStream ssl,
    CancellationToken authenticationCancellationToken,
    CancellationToken sessionCancellationToken)
  {
    var request = await ProtocolCodec.ReadAsync(ssl, authenticationCancellationToken).ConfigureAwait(false);
    if (request is null)
    {
      return;
    }

    if (request.ProtocolVersion != ProtocolCodec.CurrentVersion)
    {
      await RejectAsync(ssl, "unsupported_protocol", authenticationCancellationToken).ConfigureAwait(false);
      return;
    }

    switch (request.Type)
    {
      case "ping":
        await ProtocolCodec.WriteAsync(
          ssl,
          new WireMessage
          {
            Type = "pong",
            Success = true,
            Fingerprint = Fingerprint
          },
          authenticationCancellationToken).ConfigureAwait(false);
        return;

      case "register":
        await RegisterAsync(ssl, request, authenticationCancellationToken).ConfigureAwait(false);
        return;

      case "hello":
        await AuthenticateAsync(
          phoneClient,
          ssl,
          request,
          authenticationCancellationToken,
          sessionCancellationToken).ConfigureAwait(false);
        return;

      default:
        await RejectAsync(ssl, "unsupported_message", authenticationCancellationToken).ConfigureAwait(false);
        return;
    }
  }

  private async Task RegisterAsync(
    SslStream ssl,
    WireMessage request,
    CancellationToken cancellationToken)
  {
    if (!IsValidDeviceId(request.DeviceId)
      || string.IsNullOrWhiteSpace(request.DeviceName)
      || request.DeviceName.Length > 80
      || string.IsNullOrWhiteSpace(request.PublicKey)
      || !registrationCodes.TryConsume(request.Code))
    {
      await RejectAsync(ssl, "registration_rejected", cancellationToken).ConfigureAwait(false);
      return;
    }

    DeviceSignatureVerifier.ValidatePublicKey(request.PublicKey);
    await deviceRegistry.UpsertAsync(
      new DeviceRecord(
        request.DeviceId!,
        request.DeviceName.Trim(),
        request.PublicKey,
        DateTimeOffset.UtcNow),
      cancellationToken).ConfigureAwait(false);

    var registeredDevice = await deviceRegistry.FindAsync(
      request.DeviceId!,
      cancellationToken).ConfigureAwait(false);
    if (registeredDevice is not null)
    {
      DeviceRegistered?.Invoke(registeredDevice);
    }

    await ProtocolCodec.WriteAsync(
      ssl,
      new WireMessage
      {
        Type = "registered",
        Success = true,
        DeviceId = request.DeviceId
      },
      cancellationToken).ConfigureAwait(false);
  }

  private async Task AuthenticateAsync(
    TcpClient phoneClient,
    SslStream ssl,
    WireMessage hello,
    CancellationToken authenticationCancellationToken,
    CancellationToken sessionCancellationToken)
  {
    if (!IsValidDeviceId(hello.DeviceId))
    {
      await RejectAsync(ssl, "unknown_device", authenticationCancellationToken).ConfigureAwait(false);
      return;
    }

    var device = await deviceRegistry.FindAsync(
      hello.DeviceId!,
      authenticationCancellationToken).ConfigureAwait(false);
    if (device is null)
    {
      await RejectAsync(ssl, "unknown_device", authenticationCancellationToken).ConfigureAwait(false);
      return;
    }

    var challenge = RandomNumberGenerator.GetBytes(32);
    await ProtocolCodec.WriteAsync(
      ssl,
      new WireMessage
      {
        Type = "challenge",
        DeviceId = device.DeviceId,
        Nonce = Convert.ToBase64String(challenge),
        ExpiresAt = DateTimeOffset.UtcNow.Add(options.AuthenticationTimeout)
      },
      authenticationCancellationToken).ConfigureAwait(false);

    var authentication = await ProtocolCodec.ReadAsync(
      ssl,
      authenticationCancellationToken).ConfigureAwait(false);
    if (authentication is null
      || authentication.Type != "authenticate"
      || !string.Equals(authentication.DeviceId, device.DeviceId, StringComparison.Ordinal)
      || string.IsNullOrWhiteSpace(authentication.Signature)
      || !DeviceSignatureVerifier.Verify(device.PublicKeySpkiBase64, challenge, authentication.Signature))
    {
      await RejectAsync(ssl, "authentication_rejected", authenticationCancellationToken).ConfigureAwait(false);
      return;
    }

    await ProtocolCodec.WriteAsync(
      ssl,
      new WireMessage
      {
        Type = "authenticated",
        DeviceId = device.DeviceId,
        Success = true
      },
      authenticationCancellationToken).ConfigureAwait(false);

    while (!sessionCancellationToken.IsCancellationRequested)
    {
      var message = await ProtocolCodec.ReadAsync(ssl, sessionCancellationToken).ConfigureAwait(false);
      if (message is null || message.Type == "close")
      {
        return;
      }

      if (message.Type == "ping")
      {
        await ProtocolCodec.WriteAsync(
          ssl,
          new WireMessage { Type = "pong", Success = true },
          sessionCancellationToken).ConfigureAwait(false);
      }
      else if (message.Type == "publish_relay")
      {
        await RunRelayAsync(
          phoneClient,
          ssl,
          device.DeviceId,
          message,
          sessionCancellationToken).ConfigureAwait(false);
        return;
      }
      else
      {
        await RejectAsync(ssl, "unsupported_authenticated_message", sessionCancellationToken).ConfigureAwait(false);
        return;
      }
    }
  }

  private async Task RunRelayAsync(
    TcpClient phoneClient,
    SslStream phoneStream,
    string deviceId,
    WireMessage request,
    CancellationToken cancellationToken)
  {
    var relayConfiguration = request.RelayKind switch
    {
      "connect" => (Port: options.AdbConnectRelayPort, Slot: _connectRelaySlot),
      "pairing" => (Port: options.AdbPairingRelayPort, Slot: _pairingRelaySlot),
      _ => (Port: -1, Slot: (SemaphoreSlim?)null)
    };

    if (relayConfiguration.Port < 0 || relayConfiguration.Slot is null)
    {
      await RejectAsync(phoneStream, "invalid_relay_kind", cancellationToken).ConfigureAwait(false);
      return;
    }

    await relayConfiguration.Slot.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var listener = new TcpListener(IPAddress.Loopback, relayConfiguration.Port);
      try
      {
        listener.Start(1);
      }
      catch (SocketException)
      {
        await RejectAsync(phoneStream, "relay_port_unavailable", cancellationToken).ConfigureAwait(false);
        return;
      }

      try
      {
        var boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        await ProtocolCodec.WriteAsync(
          phoneStream,
          new WireMessage
          {
            Type = "relay_published",
            Success = true,
            DeviceId = deviceId,
            RelayKind = request.RelayKind,
            RelayPort = boundPort
          },
          cancellationToken).ConfigureAwait(false);
        RelayPublished?.Invoke(new RelayPublishedEvent(deviceId, request.RelayKind!, boundPort));

        using var localAdbClient = await WaitForLocalAdbClientAsync(
          listener,
          phoneClient,
          phoneStream,
          cancellationToken).ConfigureAwait(false);
        if (localAdbClient is null)
        {
          return;
        }
        listener.Stop();

        await ProtocolCodec.WriteAsync(
          phoneStream,
          new WireMessage
          {
            Type = "relay_start",
            Success = true,
            RelayKind = request.RelayKind,
            RelayPort = boundPort
          },
          cancellationToken).ConfigureAwait(false);

        using var relayStartTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        relayStartTimeout.CancelAfter(options.AuthenticationTimeout);
        var ready = await ProtocolCodec.ReadAsync(phoneStream, relayStartTimeout.Token).ConfigureAwait(false);
        if (ready is null || ready.Type != "relay_ready" || ready.RelayKind != request.RelayKind)
        {
          return;
        }

        await RelayStreamPump.RunAsync(
          phoneStream,
          localAdbClient.GetStream(),
          cancellationToken).ConfigureAwait(false);
      }
      finally
      {
        listener.Stop();
      }
    }
    finally
    {
      relayConfiguration.Slot.Release();
    }
  }

  private static async Task<TcpClient?> WaitForLocalAdbClientAsync(
    TcpListener listener,
    TcpClient phoneClient,
    Stream phoneStream,
    CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      if (listener.Pending())
      {
        return await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
      }

      if (phoneClient.Client.Poll(0, SelectMode.SelectRead))
      {
        var control = await ProtocolCodec.ReadAsync(phoneStream, cancellationToken).ConfigureAwait(false);
        if (control is null || control.Type == "close")
        {
          return null;
        }

        if (control.Type == "ping")
        {
          await ProtocolCodec.WriteAsync(
            phoneStream,
            new WireMessage { Type = "pong", Success = true },
            cancellationToken).ConfigureAwait(false);
        }
        else
        {
          await RejectAsync(phoneStream, "unsupported_relay_wait_message", cancellationToken)
            .ConfigureAwait(false);
          return null;
        }
      }

      await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
    }

    return null;
  }

  private static bool IsValidDeviceId(string? deviceId) =>
    !string.IsNullOrWhiteSpace(deviceId)
    && deviceId.Length <= 128
    && deviceId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

  private static ValueTask RejectAsync(
    Stream stream,
    string reason,
    CancellationToken cancellationToken) =>
    ProtocolCodec.WriteAsync(
      stream,
      new WireMessage { Type = "error", Success = false, Message = reason },
      cancellationToken);
}

public sealed record RelayPublishedEvent(string DeviceId, string RelayKind, int LocalPort);
