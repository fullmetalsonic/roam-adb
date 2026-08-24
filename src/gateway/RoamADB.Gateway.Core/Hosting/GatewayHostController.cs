using System.Net;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Registration;
using RoamADB.Gateway.Security;
using RoamADB.Gateway.Server;
using RoamADB.Gateway.Storage;

namespace RoamADB.Gateway.Hosting;

public sealed class GatewayHostController : IAsyncDisposable
{
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly GatewayPaths _paths;
  private readonly FileDeviceRegistry _deviceRegistry;
  private readonly RegistrationCodeManager _registrationCodes = new();
  private CancellationTokenSource? _cancellation;
  private GatewayServer? _server;
  private Task? _serverTask;

  public GatewayHostController(GatewayPaths paths)
  {
    _paths = paths;
    _deviceRegistry = new FileDeviceRegistry(paths.DeviceRegistryPath);
  }

  public bool IsRunning => _serverTask is { IsCompleted: false };
  public IPAddress? ListenAddress { get; private set; }
  public int ListenPort { get; private set; }
  public string? Fingerprint { get; private set; }

  public event Action<string>? StatusChanged;
  public event Action<Exception>? ClientFault;
  public event Action<DeviceRecord>? DeviceRegistered;
  public event Action<RelayPublishedEvent>? RelayPublished;

  public async Task StartTailnetAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (IsRunning)
      {
        return;
      }

      var address = await TailscaleAddress.ResolveCurrentIpv4Async(cancellationToken).ConfigureAwait(false);
      var options = new GatewayOptions
      {
        ListenAddress = address,
        Exposure = GatewayExposure.TailnetOnly
      };
      var certificate = GatewayCertificateProvider.GetOrCreateCurrentUserCertificate();
      var server = new GatewayServer(options, certificate, _deviceRegistry, _registrationCodes);
      server.ClientFault += OnClientFault;
      server.DeviceRegistered += OnDeviceRegistered;
      server.RelayPublished += OnRelayPublished;
      var cancellation = new CancellationTokenSource();

      try
      {
        var task = server.RunAsync(cancellation.Token);
        if (server.BoundPort == 0)
        {
          await Task.Yield();
        }

        if (task.IsFaulted)
        {
          await task.ConfigureAwait(false);
        }

        _server = server;
        _cancellation = cancellation;
        _serverTask = task;
        ListenAddress = address;
        ListenPort = server.BoundPort;
        Fingerprint = server.Fingerprint;
        StatusChanged?.Invoke($"Gateway running on {address}:{server.BoundPort}");
      }
      catch
      {
        cancellation.Dispose();
        await server.DisposeAsync().ConfigureAwait(false);
        throw;
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  public RegistrationPayload IssueRegistration()
  {
    if (!IsRunning || _server is null || ListenAddress is null)
    {
      throw new InvalidOperationException("Start the Gateway before creating a registration code.");
    }

    var ticket = _registrationCodes.Issue();
    return new RegistrationPayload(ListenAddress, ListenPort, _server.Fingerprint, ticket);
  }

  public ValueTask<IReadOnlyList<DeviceRecord>> ListDevicesAsync(
    CancellationToken cancellationToken = default) =>
    _deviceRegistry.ListAsync(cancellationToken);

  public ValueTask<bool> RemoveDeviceAsync(
    string deviceId,
    CancellationToken cancellationToken = default) =>
    _deviceRegistry.RemoveAsync(deviceId, cancellationToken);

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (_server is null)
      {
        ResetState();
        return;
      }

      var server = _server;
      var cancellation = _cancellation;
      var task = _serverTask;
      cancellation?.Cancel();
      if (task is not null)
      {
        try
        {
          await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
          // Normal user-requested shutdown.
        }
      }

      server.ClientFault -= OnClientFault;
      server.DeviceRegistered -= OnDeviceRegistered;
      server.RelayPublished -= OnRelayPublished;
      await server.DisposeAsync().ConfigureAwait(false);
      cancellation?.Dispose();
      ResetState();
      StatusChanged?.Invoke("Gateway stopped");
    }
    finally
    {
      _gate.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync().ConfigureAwait(false);
    _gate.Dispose();
  }

  private void OnClientFault(Exception exception) => ClientFault?.Invoke(exception);
  private void OnDeviceRegistered(DeviceRecord device) => DeviceRegistered?.Invoke(device);
  private void OnRelayPublished(RelayPublishedEvent relay) => RelayPublished?.Invoke(relay);

  private void ResetState()
  {
    _server = null;
    _cancellation = null;
    _serverTask = null;
    ListenAddress = null;
    ListenPort = 0;
    Fingerprint = null;
  }
}
