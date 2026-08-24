using System.Net;
using System.Text;
using RoamADB.Gateway.Security;

namespace RoamADB.Gateway.Registration;

public sealed record RegistrationPayload(
  IPAddress Host,
  int Port,
  string Fingerprint,
  RegistrationTicket Ticket,
  string Mode = RegistrationPayload.ExistingVpnMode)
{
  public const string Scheme = "roamadb";
  public const string HostName = "register";
  public const string ExistingVpnMode = "existing-vpn-adb-only";
  public const int ProtocolVersion = 1;

  public string ToUri()
  {
    if (Host.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
    {
      throw new InvalidOperationException("Registration QR currently requires an IPv4 Gateway address.");
    }

    if (Port is < 1 or > 65535)
    {
      throw new ArgumentOutOfRangeException(nameof(Port));
    }

    var normalizedFingerprint = NormalizeFingerprint(Fingerprint);
    if (!Ticket.Code.All(char.IsAsciiDigit) || Ticket.Code.Length != 6)
    {
      throw new InvalidOperationException("Registration code must contain six digits.");
    }

    if (!string.Equals(Mode, ExistingVpnMode, StringComparison.Ordinal))
    {
      throw new InvalidOperationException("The registration mode is unsupported.");
    }

    var values = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["v"] = ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ["host"] = Host.ToString(),
      ["port"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
      ["fingerprint"] = normalizedFingerprint,
      ["code"] = Ticket.Code,
      ["mode"] = Mode,
      ["expires"] = Ticket.ExpiresAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)
    };

    var query = string.Join(
      "&",
      values.Select(pair => $"{Escape(pair.Key)}={Escape(pair.Value)}"));
    return $"{Scheme}://{HostName}?{query}";
  }

  private static string NormalizeFingerprint(string value)
  {
    var builder = new StringBuilder(64);
    foreach (var character in value)
    {
      if (character is ':' or ' ')
      {
        continue;
      }

      builder.Append(char.ToUpperInvariant(character));
    }

    var normalized = builder.ToString();
    if (normalized.Length != 64 || !normalized.All(Uri.IsHexDigit))
    {
      throw new InvalidOperationException("Gateway fingerprint must be a SHA-256 hexadecimal value.");
    }

    return normalized;
  }

  private static string Escape(string value) => Uri.EscapeDataString(value);
}
