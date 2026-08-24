using System.Diagnostics;
using System.IO;
using System.Text;
using RoamADB.Gateway.Diagnostics;

namespace RoamADB.Gateway.Desktop.Services;

public sealed record ToolCommandResult(bool Success, string DisplayCommand, string Output);

public sealed class AndroidToolService
{
  public string? AdbPath => ToolLocator.FindAdb();
  public string? ScrcpyPath => ToolLocator.FindScrcpy();

  public Task<ToolCommandResult> PairAsync(string code, CancellationToken cancellationToken = default)
  {
    if (code.Length != 6 || !code.All(char.IsAsciiDigit))
    {
      throw new ArgumentException("Android에 표시된 6자리 페어링 코드를 입력하세요.", nameof(code));
    }

    return RunAdbAsync(["pair", "127.0.0.1:47158", code], TimeSpan.FromSeconds(20), cancellationToken);
  }

  public Task<ToolCommandResult> ConnectAsync(CancellationToken cancellationToken = default) =>
    RunAdbAsync(["connect", "127.0.0.1:47157"], TimeSpan.FromSeconds(15), cancellationToken);

  public Task<ToolCommandResult> DisconnectAsync(CancellationToken cancellationToken = default) =>
    RunAdbAsync(["disconnect", "127.0.0.1:47157"], TimeSpan.FromSeconds(10), cancellationToken);

  public Task<ToolCommandResult> ListDevicesAsync(CancellationToken cancellationToken = default) =>
    RunAdbAsync(["devices", "-l"], TimeSpan.FromSeconds(10), cancellationToken);

  public ToolCommandResult LaunchScrcpy()
  {
    var executable = ScrcpyPath
      ?? throw new FileNotFoundException("scrcpy.exe를 찾지 못했습니다. PATH 또는 프로그램의 scrcpy 폴더에 설치하세요.");
    var startInfo = CreateStartInfo(executable, ["--serial", "127.0.0.1:47157"]);
    startInfo.UseShellExecute = false;
    startInfo.CreateNoWindow = false;
    _ = Process.Start(startInfo)
      ?? throw new InvalidOperationException("scrcpy를 시작하지 못했습니다.");
    return new ToolCommandResult(true, "scrcpy --serial 127.0.0.1:47157", "scrcpy를 시작했습니다.");
  }

  private async Task<ToolCommandResult> RunAdbAsync(
    IReadOnlyList<string> arguments,
    TimeSpan timeout,
    CancellationToken cancellationToken)
  {
    var executable = AdbPath
      ?? throw new FileNotFoundException("adb.exe를 찾지 못했습니다. Android SDK Platform-Tools를 설치하세요.");
    var startInfo = CreateStartInfo(executable, arguments);
    startInfo.RedirectStandardOutput = true;
    startInfo.RedirectStandardError = true;
    startInfo.CreateNoWindow = true;

    using var process = Process.Start(startInfo)
      ?? throw new InvalidOperationException("ADB를 시작하지 못했습니다.");
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    deadline.CancelAfter(timeout);
    var outputTask = process.StandardOutput.ReadToEndAsync(deadline.Token);
    var errorTask = process.StandardError.ReadToEndAsync(deadline.Token);
    try
    {
      await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
      TryTerminate(process);
      throw new TimeoutException($"ADB 명령이 {timeout.TotalSeconds:0}초 안에 끝나지 않았습니다.");
    }

    var output = (await outputTask.ConfigureAwait(false)).Trim();
    var error = (await errorTask.ConfigureAwait(false)).Trim();
    var combined = new StringBuilder();
    if (!string.IsNullOrWhiteSpace(output))
    {
      combined.AppendLine(output);
    }

    if (!string.IsNullOrWhiteSpace(error))
    {
      combined.AppendLine(error);
    }

    return new ToolCommandResult(
      process.ExitCode == 0,
      $"adb {string.Join(' ', arguments.Select(RedactPairingCode))}",
      combined.ToString().Trim());
  }

  private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
  {
    var startInfo = new ProcessStartInfo
    {
      FileName = executable,
      UseShellExecute = false
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    return startInfo;
  }

  private static string RedactPairingCode(string value) =>
    value.Length == 6 && value.All(char.IsAsciiDigit) ? "******" : value;

  private static void TryTerminate(Process process)
  {
    try
    {
      if (!process.HasExited)
      {
        process.Kill(entireProcessTree: true);
      }
    }
    catch (InvalidOperationException)
    {
      // Process already exited.
    }
  }
}
