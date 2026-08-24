using System.Text.Json;

namespace RoamADB.Gateway.Security;

public sealed class FileDeviceRegistry(string path) : IDeviceRegistry
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true
  };

  private readonly SemaphoreSlim _mutex = new(1, 1);

  public async ValueTask<DeviceRecord?> FindAsync(string deviceId, CancellationToken cancellationToken)
  {
    var devices = await ReadAsync(cancellationToken).ConfigureAwait(false);
    return devices.FirstOrDefault(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal));
  }

  public async ValueTask<IReadOnlyList<DeviceRecord>> ListAsync(CancellationToken cancellationToken) =>
    await ReadAsync(cancellationToken).ConfigureAwait(false);

  public async ValueTask UpsertAsync(DeviceRecord device, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var devices = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
      devices.RemoveAll(existing => string.Equals(existing.DeviceId, device.DeviceId, StringComparison.Ordinal));
      devices.Add(device);
      await WriteUnlockedAsync(devices, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  public async ValueTask<bool> RemoveAsync(string deviceId, CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      var devices = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
      var removed = devices.RemoveAll(
        existing => string.Equals(existing.DeviceId, deviceId, StringComparison.Ordinal)) > 0;
      if (removed)
      {
        await WriteUnlockedAsync(devices, cancellationToken).ConfigureAwait(false);
      }

      return removed;
    }
    finally
    {
      _mutex.Release();
    }
  }

  private async ValueTask<IReadOnlyList<DeviceRecord>> ReadAsync(CancellationToken cancellationToken)
  {
    await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      _mutex.Release();
    }
  }

  private async ValueTask<List<DeviceRecord>> ReadUnlockedAsync(CancellationToken cancellationToken)
  {
    if (!File.Exists(path))
    {
      return [];
    }

    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<List<DeviceRecord>>(stream, JsonOptions, cancellationToken)
        .ConfigureAwait(false)
      ?? [];
  }

  private async ValueTask WriteUnlockedAsync(
    IReadOnlyCollection<DeviceRecord> devices,
    CancellationToken cancellationToken)
  {
    var directory = Path.GetDirectoryName(path)
      ?? throw new InvalidOperationException("The registry path has no parent directory.");
    Directory.CreateDirectory(directory);

    var temporaryPath = path + ".tmp";
    await using (var stream = File.Create(temporaryPath))
    {
      await JsonSerializer.SerializeAsync(stream, devices, JsonOptions, cancellationToken)
        .ConfigureAwait(false);
      await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    File.Move(temporaryPath, path, true);
  }
}
