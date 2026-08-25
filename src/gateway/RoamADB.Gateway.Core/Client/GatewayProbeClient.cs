using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using RoamADB.Gateway.Protocol;
using RoamADB.Gateway.Security;

namespace RoamADB.Gateway.Client;

public sealed class GatewayProbeClient(string host, int port, string expectedFingerprint)
{
  public async Task<WireMessage> PingAsync(CancellationToken cancellationToken)
  {
    await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
    await ProtocolCodec.WriteAsync(
      connection,
      new WireMessage { Type = "ping" },
      cancellationToken).ConfigureAwait(false);
    return await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
      ?? throw new IOException("The Gateway closed the ping connection without a response.");
  }

  public async Task<WireMessage> RegisterAsync(
    RegistrationRequest registration,
    CancellationToken cancellationToken)
  {
    await using var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
    await ProtocolCodec.WriteAsync(
      connection,
      new WireMessage
      {
        Type = "register",
        DeviceId = registration.DeviceId,
        DeviceName = registration.DeviceName,
        PublicKey = registration.PublicKeySpkiBase64,
        Code = registration.Code
      },
      cancellationToken).ConfigureAwait(false);
    return await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
      ?? throw new IOException("The Gateway closed the registration connection without a response.");
  }

  public async Task<WireMessage> AuthenticateAsync(
    string deviceId,
    ECDsa privateKey,
    CancellationToken cancellationToken)
  {
    var (connection, response) = await AuthenticateConnectionAsync(
      deviceId,
      privateKey,
      cancellationToken).ConfigureAwait(false);
    await connection.DisposeAsync().ConfigureAwait(false);
    return response;
  }

  public async Task<GatewayProbeSession> OpenAuthenticatedSessionAsync(
    string deviceId,
    ECDsa privateKey,
    CancellationToken cancellationToken)
  {
    var (connection, response) = await AuthenticateConnectionAsync(
      deviceId,
      privateKey,
      cancellationToken).ConfigureAwait(false);
    if (response.Type != "authenticated" || response.Success != true)
    {
      await connection.DisposeAsync().ConfigureAwait(false);
      throw new AuthenticationException(response.Message ?? "Gateway authentication failed.");
    }

    return new GatewayProbeSession(connection);
  }

  private async Task<(Stream Connection, WireMessage Response)> AuthenticateConnectionAsync(
    string deviceId,
    ECDsa privateKey,
    CancellationToken cancellationToken)
  {
    var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      await ProtocolCodec.WriteAsync(
        connection,
        new WireMessage { Type = "hello", DeviceId = deviceId },
        cancellationToken).ConfigureAwait(false);
      var challenge = await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
        ?? throw new IOException("The Gateway did not return an authentication challenge.");

      if (challenge.Type != "challenge" || string.IsNullOrWhiteSpace(challenge.Nonce))
      {
        return (connection, challenge);
      }

      var nonce = Convert.FromBase64String(challenge.Nonce);
      var signature = privateKey.SignData(
        nonce,
        HashAlgorithmName.SHA256,
        DSASignatureFormat.Rfc3279DerSequence);
      await ProtocolCodec.WriteAsync(
        connection,
        new WireMessage
        {
          Type = "authenticate",
          DeviceId = deviceId,
          Signature = Convert.ToBase64String(signature)
        },
        cancellationToken).ConfigureAwait(false);

      var response = await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
        ?? throw new IOException("The Gateway closed the authentication connection without a response.");
      return (connection, response);
    }
    catch
    {
      await connection.DisposeAsync().ConfigureAwait(false);
      throw;
    }
  }

  private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
  {
    var tcp = new TcpClient();
    try
    {
      await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
      var ssl = new SslStream(
        tcp.GetStream(),
        false,
        (_, certificate, _, _) => certificate is not null && FingerprintMatches(certificate));
      await ssl.AuthenticateAsClientAsync(
        new SslClientAuthenticationOptions
        {
          TargetHost = "RoamADB Gateway",
          EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
          CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        },
        cancellationToken).ConfigureAwait(false);
      return new OwnedSslStream(ssl, tcp);
    }
    catch
    {
      tcp.Dispose();
      throw;
    }
  }

  private bool FingerprintMatches(X509Certificate certificate)
  {
    var actual = SHA256.HashData(certificate.GetRawCertData());
    byte[] expected;
    try
    {
      expected = Convert.FromHexString(expectedFingerprint.Replace(":", string.Empty, StringComparison.Ordinal));
    }
    catch (FormatException)
    {
      return false;
    }

    return actual.Length == expected.Length
      && CryptographicOperations.FixedTimeEquals(actual, expected);
  }

  private sealed class OwnedSslStream(SslStream inner, TcpClient owner) : SslStreamDecorator(inner)
  {
    protected override void Dispose(bool disposing)
    {
      base.Dispose(disposing);
      if (disposing)
      {
        owner.Dispose();
      }
    }

    public override async ValueTask DisposeAsync()
    {
      await base.DisposeAsync().ConfigureAwait(false);
      owner.Dispose();
    }
  }
}

public sealed class GatewayProbeSession(Stream connection) : IAsyncDisposable
{
  private bool _rawRelayActive;

  public async Task<WireMessage> PingAsync(CancellationToken cancellationToken)
  {
    await ProtocolCodec.WriteAsync(
      connection,
      new WireMessage { Type = "ping" },
      cancellationToken).ConfigureAwait(false);
    return await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
      ?? throw new IOException("The Gateway closed the authenticated session without a response.");
  }

  public async Task<WireMessage> PublishRelayAsync(
    string relayKind,
    CancellationToken cancellationToken)
  {
    await ProtocolCodec.WriteAsync(
      connection,
      new WireMessage { Type = "publish_relay", RelayKind = relayKind },
      cancellationToken).ConfigureAwait(false);
    return await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
      ?? throw new IOException("The Gateway closed the relay publication without a response.");
  }

  public async Task<WireMessage> AcceptRelayAsync(CancellationToken cancellationToken)
  {
    var start = await ProtocolCodec.ReadAsync(connection, cancellationToken).ConfigureAwait(false)
      ?? throw new IOException("The Gateway closed the relay before it started.");
    if (start.Type != "relay_start" || string.IsNullOrWhiteSpace(start.RelayKind))
    {
      return start;
    }

    await ProtocolCodec.WriteAsync(
      connection,
      new WireMessage { Type = "relay_ready", RelayKind = start.RelayKind },
      cancellationToken).ConfigureAwait(false);
    _rawRelayActive = true;
    return start;
  }

  public ValueTask<int> ReadRawAsync(
    Memory<byte> buffer,
    CancellationToken cancellationToken) =>
    connection.ReadAsync(buffer, cancellationToken);

  public ValueTask WriteRawAsync(
    ReadOnlyMemory<byte> buffer,
    CancellationToken cancellationToken) =>
    connection.WriteAsync(buffer, cancellationToken);

  public async ValueTask DisposeAsync()
  {
    try
    {
      if (!_rawRelayActive)
      {
        await ProtocolCodec.WriteAsync(
          connection,
          new WireMessage { Type = "close" },
          CancellationToken.None).ConfigureAwait(false);
      }
    }
    catch (IOException)
    {
      // The peer may already have closed the session.
    }
    finally
    {
      await connection.DisposeAsync().ConfigureAwait(false);
    }
  }
}

public sealed record RegistrationRequest(
  string DeviceId,
  string DeviceName,
  string PublicKeySpkiBase64,
  string Code);
