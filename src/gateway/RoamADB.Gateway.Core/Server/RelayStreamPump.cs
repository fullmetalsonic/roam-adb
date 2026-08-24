namespace RoamADB.Gateway.Server;

internal static class RelayStreamPump
{
  public static async Task RunAsync(
    Stream phoneStream,
    Stream localAdbStream,
    CancellationToken cancellationToken)
  {
    using var relayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    var phoneToAdb = CopyAsync(phoneStream, localAdbStream, relayCancellation.Token);
    var adbToPhone = CopyAsync(localAdbStream, phoneStream, relayCancellation.Token);

    await Task.WhenAny(phoneToAdb, adbToPhone).ConfigureAwait(false);
    await relayCancellation.CancelAsync().ConfigureAwait(false);

    await IgnoreExpectedShutdownAsync(phoneToAdb).ConfigureAwait(false);
    await IgnoreExpectedShutdownAsync(adbToPhone).ConfigureAwait(false);
  }

  private static async Task CopyAsync(
    Stream source,
    Stream destination,
    CancellationToken cancellationToken)
  {
    await source.CopyToAsync(destination, 65_536, cancellationToken).ConfigureAwait(false);
    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
  }

  private static async Task IgnoreExpectedShutdownAsync(Task task)
  {
    try
    {
      await task.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
      // The other half of the relay completed first.
    }
    catch (IOException)
    {
      // Either relay endpoint closed its socket.
    }
  }
}
