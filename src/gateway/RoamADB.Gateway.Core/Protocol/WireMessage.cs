namespace RoamADB.Gateway.Protocol;

public sealed record WireMessage
{
  public required string Type { get; init; }
  public int ProtocolVersion { get; init; } = 1;
  public string? RequestId { get; init; }
  public string? DeviceId { get; init; }
  public string? DeviceName { get; init; }
  public string? PublicKey { get; init; }
  public string? Code { get; init; }
  public string? Nonce { get; init; }
  public string? Signature { get; init; }
  public string? Fingerprint { get; init; }
  public string? RelayKind { get; init; }
  public int? RelayPort { get; init; }
  public string? Message { get; init; }
  public bool? Success { get; init; }
  public DateTimeOffset? ExpiresAt { get; init; }
}
