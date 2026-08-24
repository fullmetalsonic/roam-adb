using System.Collections.Concurrent;

namespace RoamADB.Gateway.Security;

public sealed class InMemoryDeviceRegistry : IDeviceRegistry
{
  private readonly ConcurrentDictionary<string, DeviceRecord> _devices = new(StringComparer.Ordinal);

  public ValueTask<DeviceRecord?> FindAsync(string deviceId, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    _devices.TryGetValue(deviceId, out var device);
    return ValueTask.FromResult(device);
  }

  public ValueTask<IReadOnlyList<DeviceRecord>> ListAsync(CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    IReadOnlyList<DeviceRecord> result = _devices.Values
      .OrderBy(device => device.DeviceName, StringComparer.OrdinalIgnoreCase)
      .ToArray();
    return ValueTask.FromResult(result);
  }

  public ValueTask UpsertAsync(DeviceRecord device, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    _devices[device.DeviceId] = device;
    return ValueTask.CompletedTask;
  }

  public ValueTask<bool> RemoveAsync(string deviceId, CancellationToken cancellationToken)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return ValueTask.FromResult(_devices.TryRemove(deviceId, out _));
  }
}
