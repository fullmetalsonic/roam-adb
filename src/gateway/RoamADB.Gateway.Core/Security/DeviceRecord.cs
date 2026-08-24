namespace RoamADB.Gateway.Security;

public sealed record DeviceRecord(
  string DeviceId,
  string DeviceName,
  string PublicKeySpkiBase64,
  DateTimeOffset RegisteredAt);

public interface IDeviceRegistry
{
  ValueTask<DeviceRecord?> FindAsync(string deviceId, CancellationToken cancellationToken);
  ValueTask<IReadOnlyList<DeviceRecord>> ListAsync(CancellationToken cancellationToken);
  ValueTask UpsertAsync(DeviceRecord device, CancellationToken cancellationToken);
  ValueTask<bool> RemoveAsync(string deviceId, CancellationToken cancellationToken);
}
