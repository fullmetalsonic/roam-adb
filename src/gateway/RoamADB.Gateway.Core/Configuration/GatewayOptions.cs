using System.Net;

namespace RoamADB.Gateway.Configuration;

public enum GatewayExposure
{
  LoopbackOnly,
  TailnetOnly
}

public sealed record GatewayOptions
{
  public const int DefaultPort = 47156;
  public const int DefaultAdbConnectRelayPort = 47157;
  public const int DefaultAdbPairingRelayPort = 47158;

  public IPAddress ListenAddress { get; init; } = IPAddress.Loopback;
  public GatewayExposure Exposure { get; init; } = GatewayExposure.LoopbackOnly;
  public int Port { get; init; } = DefaultPort;
  public int MaxConcurrentClients { get; init; } = 8;
  public TimeSpan AuthenticationTimeout { get; init; } = TimeSpan.FromSeconds(15);
  public int AdbConnectRelayPort { get; init; } = DefaultAdbConnectRelayPort;
  public int AdbPairingRelayPort { get; init; } = DefaultAdbPairingRelayPort;

  public void Validate()
  {
    if (!Enum.IsDefined(Exposure))
    {
      throw new InvalidOperationException("The Gateway exposure policy is invalid.");
    }

    if (ListenAddress.Equals(IPAddress.Any) || ListenAddress.Equals(IPAddress.IPv6Any))
    {
      throw new InvalidOperationException(
        "Wildcard Gateway listeners are intentionally blocked.");
    }

    if (Exposure == GatewayExposure.LoopbackOnly && !IPAddress.IsLoopback(ListenAddress))
    {
      throw new InvalidOperationException(
        "Loopback-only mode requires a loopback listener address.");
    }

    if (Exposure == GatewayExposure.TailnetOnly && !TailscaleAddress.IsTailnetAddress(ListenAddress))
    {
      throw new InvalidOperationException(
        "Tailnet-only mode requires one exact address from Tailscale's reserved ranges.");
    }

    if (Port is < 0 or > 65535)
    {
      throw new ArgumentOutOfRangeException(nameof(Port));
    }

    if (AdbConnectRelayPort is < 0 or > 65535)
    {
      throw new ArgumentOutOfRangeException(nameof(AdbConnectRelayPort));
    }

    if (AdbPairingRelayPort is < 0 or > 65535)
    {
      throw new ArgumentOutOfRangeException(nameof(AdbPairingRelayPort));
    }

    if (AdbConnectRelayPort != 0
      && AdbPairingRelayPort != 0
      && AdbConnectRelayPort == AdbPairingRelayPort)
    {
      throw new InvalidOperationException("ADB connect and pairing relay ports must be different.");
    }

    if (Port != 0
      && (Port == AdbConnectRelayPort || Port == AdbPairingRelayPort))
    {
      throw new InvalidOperationException("The Gateway listener and local ADB relay ports must be different.");
    }

    if (MaxConcurrentClients is < 1 or > 64)
    {
      throw new ArgumentOutOfRangeException(nameof(MaxConcurrentClients));
    }

    if (AuthenticationTimeout <= TimeSpan.Zero || AuthenticationTimeout > TimeSpan.FromMinutes(5))
    {
      throw new ArgumentOutOfRangeException(nameof(AuthenticationTimeout));
    }
  }
}
