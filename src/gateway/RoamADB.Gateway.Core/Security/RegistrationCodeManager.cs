using System.Security.Cryptography;
using System.Text;

namespace RoamADB.Gateway.Security;

public sealed record RegistrationTicket(string Code, DateTimeOffset ExpiresAt);

public sealed class RegistrationCodeManager(
  TimeProvider? timeProvider = null,
  TimeSpan? lifetime = null,
  int maximumAttempts = 5)
{
  private readonly Lock _gate = new();
  private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
  private readonly TimeSpan _lifetime = lifetime ?? TimeSpan.FromMinutes(2);
  private byte[]? _digest;
  private DateTimeOffset _expiresAt;
  private int _attemptsRemaining;

  public RegistrationTicket Issue()
  {
    var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
    var expiresAt = _timeProvider.GetUtcNow().Add(_lifetime);

    lock (_gate)
    {
      _digest = Digest(code);
      _expiresAt = expiresAt;
      _attemptsRemaining = maximumAttempts;
    }

    return new RegistrationTicket(code, expiresAt);
  }

  public bool TryConsume(string? code)
  {
    if (string.IsNullOrWhiteSpace(code))
    {
      return false;
    }

    lock (_gate)
    {
      if (_digest is null || _timeProvider.GetUtcNow() > _expiresAt || _attemptsRemaining <= 0)
      {
        Clear();
        return false;
      }

      _attemptsRemaining--;
      var candidate = Digest(code);
      var valid = CryptographicOperations.FixedTimeEquals(_digest, candidate);
      CryptographicOperations.ZeroMemory(candidate);

      if (valid || _attemptsRemaining <= 0)
      {
        Clear();
      }

      return valid;
    }
  }

  private static byte[] Digest(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

  private void Clear()
  {
    if (_digest is not null)
    {
      CryptographicOperations.ZeroMemory(_digest);
    }

    _digest = null;
    _expiresAt = default;
    _attemptsRemaining = 0;
  }
}
