using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RoamADB.Gateway.Configuration;
using RoamADB.Gateway.Desktop.Services;
using RoamADB.Gateway.Diagnostics;
using RoamADB.Gateway.Hosting;
using RoamADB.Gateway.Registration;
using RoamADB.Gateway.Security;
using RoamADB.Gateway.Server;
using RoamADB.Gateway.Storage;

namespace RoamADB.Gateway.Desktop;

public partial class MainWindow : Window
{
  private static readonly Regex DigitsRegex = new("^[0-9]+$", RegexOptions.CultureInvariant);
  private readonly GatewayPaths _paths = GatewayPaths.ForCurrentUser();
  private readonly GatewayHostController _gateway;
  private readonly AndroidToolService _androidTools = new();
  private readonly DispatcherTimer _registrationTimer = new() { Interval = TimeSpan.FromSeconds(1) };
  private readonly ObservableCollection<string> _logs = [];
  private RegistrationPayload? _registration;
  private bool _allowClose;
  private bool _shutdownStarted;

  public MainWindow()
  {
    InitializeComponent();
    _gateway = new GatewayHostController(_paths);
    _gateway.StatusChanged += OnGatewayStatusChanged;
    _gateway.ClientFault += OnGatewayClientFault;
    _gateway.DeviceRegistered += OnDeviceRegistered;
    _gateway.RelayPublished += OnRelayPublished;
    _registrationTimer.Tick += RegistrationTimer_Tick;
    LogList.ItemsSource = _logs;
    PcNameText.Text = Environment.MachineName;
    Loaded += MainWindow_Loaded;
    Closing += MainWindow_Closing;
  }

  private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
  {
    await RunDiagnosticsAsync();
    await RefreshDevicesAsync();
  }

  private async void StartGateway_Click(object sender, RoutedEventArgs e)
  {
    await RunUiOperationAsync(async () =>
    {
      FooterStatusText.Text = "Tailscale 주소를 확인하고 Gateway를 켜는 중…";
      await _gateway.StartTailnetAsync();
      UpdateGatewayControls();
      AddLog($"Gateway 시작: {_gateway.ListenAddress}:{_gateway.ListenPort}");
      await RunDiagnosticsAsync();
    }, "Gateway를 시작하지 못했습니다.");
  }

  private async void StopGateway_Click(object sender, RoutedEventArgs e)
  {
    await RunUiOperationAsync(async () =>
    {
      FooterStatusText.Text = "Gateway를 안전하게 끄는 중…";
      await _gateway.StopAsync();
      ClearRegistration("Gateway가 꺼져 등록 정보가 폐기되었습니다.");
      UpdateGatewayControls();
      AddLog("Gateway 중지");
      await RunDiagnosticsAsync();
    }, "Gateway를 중지하지 못했습니다.");
  }

  private void IssueRegistration_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      _registration = _gateway.IssueRegistration();
      var payload = _registration.ToUri();
      RegistrationCodeText.Text = _registration.Ticket.Code;
      RegistrationAddressText.Text = $"{_registration.Host}:{_registration.Port}";
      FingerprintText.Text = FormatFingerprint(_registration.Fingerprint);
      RegistrationQrImage.Source = QrImageService.Create(payload);
      QrPlaceholderText.Visibility = Visibility.Collapsed;
      _registrationTimer.Start();
      UpdateRegistrationCountdown();
      AddLog("2분 일회용 등록 코드와 QR 생성");
      FooterStatusText.Text = "휴대폰 RoamADB 앱에서 QR을 스캔하거나 값을 수동 입력하세요.";
    }
    catch (Exception exception)
    {
      ShowError("등록 정보를 만들지 못했습니다.", exception);
    }
  }

  private async void RunDiagnostics_Click(object sender, RoutedEventArgs e) =>
    await RunDiagnosticsAsync();

  private async void RefreshDevices_Click(object sender, RoutedEventArgs e) =>
    await RefreshDevicesAsync();

  private async void RemoveDevice_Click(object sender, RoutedEventArgs e)
  {
    if (DevicesGrid.SelectedItem is not DeviceRow selected)
    {
      MessageBox.Show("등록 해제할 휴대폰을 목록에서 먼저 선택하세요.", "RoamADB Gateway");
      return;
    }

    var answer = MessageBox.Show(
      $"'{selected.DeviceName}'의 Gateway 등록을 해제할까요?\n다시 쓰려면 새 QR/코드로 등록해야 합니다.",
      "휴대폰 등록 해제",
      MessageBoxButton.YesNo,
      MessageBoxImage.Warning);
    if (answer != MessageBoxResult.Yes)
    {
      return;
    }

    await RunUiOperationAsync(async () =>
    {
      var removed = await _gateway.RemoveDeviceAsync(selected.DeviceId);
      AddLog(removed ? $"휴대폰 등록 해제: {selected.DeviceName}" : "등록 해제 대상이 이미 없음");
      await RefreshDevicesAsync();
    }, "휴대폰 등록을 해제하지 못했습니다.");
  }

  private async void PairAdb_Click(object sender, RoutedEventArgs e) =>
    await RunToolAsync(() => _androidTools.PairAsync(PairingCodeInput.Text.Trim()));

  private async void ConnectAdb_Click(object sender, RoutedEventArgs e) =>
    await RunToolAsync(() => _androidTools.ConnectAsync());

  private async void DisconnectAdb_Click(object sender, RoutedEventArgs e) =>
    await RunToolAsync(() => _androidTools.DisconnectAsync());

  private async void ListAdbDevices_Click(object sender, RoutedEventArgs e) =>
    await RunToolAsync(() => _androidTools.ListDevicesAsync());

  private void LaunchScrcpy_Click(object sender, RoutedEventArgs e)
  {
    try
    {
      var result = _androidTools.LaunchScrcpy();
      DisplayToolResult(result);
    }
    catch (Exception exception)
    {
      ShowError("scrcpy를 열지 못했습니다.", exception);
    }
  }

  private void DigitsOnly_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
    e.Handled = !DigitsRegex.IsMatch(e.Text);

  private void CopyCode_Click(object sender, RoutedEventArgs e) =>
    CopyValue(RegistrationCodeText.Text is "------" ? null : RegistrationCodeText.Text, "등록 코드");

  private void CopyAddress_Click(object sender, RoutedEventArgs e) =>
    CopyValue(RegistrationAddressText.Text is "-" ? null : RegistrationAddressText.Text, "Gateway 주소");

  private void CopyFingerprint_Click(object sender, RoutedEventArgs e) =>
    CopyValue(_registration?.Fingerprint, "인증서 지문");

  private void RegistrationTimer_Tick(object? sender, EventArgs e) => UpdateRegistrationCountdown();

  private void UpdateRegistrationCountdown()
  {
    if (_registration is null)
    {
      return;
    }

    var remaining = _registration.Ticket.ExpiresAt - DateTimeOffset.UtcNow;
    if (remaining <= TimeSpan.Zero)
    {
      ClearRegistration("만료됨 — 새 등록 정보가 필요합니다.");
      AddLog("일회용 등록 코드 만료");
      return;
    }

    RegistrationExpiryText.Text = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00} 후 만료";
  }

  private async Task RunDiagnosticsAsync()
  {
    try
    {
      var checks = await Task.Run(() => GatewayDoctor.Run(_paths, GatewayOptions.DefaultPort));
      DiagnosticsList.ItemsSource = checks.Select(check => new DiagnosticRow(
        $"{(check.Passed ? "정상" : check.Required ? "오류" : "확인")}: {KoreanCheckName(check.Name)}",
        check.Detail)).ToArray();
      var requiredFailures = checks.Count(check => check.Required && !check.Passed);
      var adb = _androidTools.AdbPath is null ? "ADB 미발견" : "ADB 발견";
      var scrcpy = _androidTools.ScrcpyPath is null ? "scrcpy 선택 사항" : "scrcpy 발견";
      FooterStatusText.Text = requiredFailures == 0
        ? $"필수 진단 정상 · {adb} · {scrcpy}"
        : $"필수 진단 오류 {requiredFailures}건 — 진단 탭을 확인하세요.";
      AddLog($"진단 완료: 필수 오류 {requiredFailures}건");
    }
    catch (Exception exception)
    {
      ShowError("PC 준비 상태를 진단하지 못했습니다.", exception);
    }
  }

  private async Task RefreshDevicesAsync()
  {
    try
    {
      var devices = await _gateway.ListDevicesAsync();
      DevicesGrid.ItemsSource = devices
        .OrderByDescending(device => device.RegisteredAt)
        .Select(device => new DeviceRow(
          device.DeviceId,
          device.DeviceName,
          device.RegisteredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
        .ToArray();
    }
    catch (Exception exception)
    {
      ShowError("등록된 휴대폰 목록을 읽지 못했습니다.", exception);
    }
  }

  private async Task RunToolAsync(Func<Task<ToolCommandResult>> operation)
  {
    try
    {
      FooterStatusText.Text = "ADB 명령 실행 중…";
      var result = await operation();
      DisplayToolResult(result);
    }
    catch (Exception exception)
    {
      ShowError("ADB 작업을 완료하지 못했습니다.", exception);
    }
  }

  private void DisplayToolResult(ToolCommandResult result)
  {
    AdbOutputText.Text = $"> {result.DisplayCommand}\r\n{result.Output}".TrimEnd();
    FooterStatusText.Text = result.Success ? "ADB 작업이 완료되었습니다." : "ADB가 오류를 반환했습니다. 결과를 확인하세요.";
    AddLog($"ADB 작업: {result.DisplayCommand} ({(result.Success ? "성공" : "오류")})");
    PairingCodeInput.Clear();
  }

  private async Task RunUiOperationAsync(Func<Task> operation, string errorTitle)
  {
    StartGatewayButton.IsEnabled = false;
    StopGatewayButton.IsEnabled = false;
    IssueRegistrationButton.IsEnabled = false;
    try
    {
      await operation();
    }
    catch (Exception exception)
    {
      ShowError(errorTitle, exception);
    }
    finally
    {
      UpdateGatewayControls();
    }
  }

  private void UpdateGatewayControls()
  {
    var running = _gateway.IsRunning;
    StartGatewayButton.IsEnabled = !running;
    StopGatewayButton.IsEnabled = running;
    IssueRegistrationButton.IsEnabled = running;
    HeaderStatusText.Text = running ? "● 실행 중" : "● 꺼짐";
    HeaderStatusBorder.Background = new SolidColorBrush(
      (Color)ColorConverter.ConvertFromString(running ? "#137A4E" : "#3F4C63"));
    GatewayAddressText.Text = running
      ? $"{_gateway.ListenAddress}:{_gateway.ListenPort} (Tailscale 전용)"
      : "Gateway를 먼저 켜세요";
  }

  private void ClearRegistration(string reason)
  {
    _registration = null;
    _registrationTimer.Stop();
    RegistrationCodeText.Text = "------";
    RegistrationExpiryText.Text = reason;
    RegistrationAddressText.Text = "-";
    FingerprintText.Text = "-";
    RegistrationQrImage.Source = null;
    QrPlaceholderText.Visibility = Visibility.Visible;
  }

  private void CopyValue(string? value, string label)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      MessageBox.Show($"복사할 {label}가 없습니다.", "RoamADB Gateway");
      return;
    }

    try
    {
      Clipboard.SetText(value);
      FooterStatusText.Text = $"{label}를 클립보드에 복사했습니다.";
    }
    catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ExternalException)
    {
      ShowError($"{label}를 복사하지 못했습니다.", exception);
    }
  }

  private void OnGatewayStatusChanged(string status) => Dispatcher.Invoke(() => AddLog(status));

  private void OnGatewayClientFault(Exception exception) => Dispatcher.Invoke(() =>
  {
    AddLog($"클라이언트 오류: {exception.GetType().Name} — {exception.Message}");
    FooterStatusText.Text = "휴대폰 연결 오류가 발생했습니다. 진단 기록을 확인하세요.";
  });

  private void OnDeviceRegistered(DeviceRecord device) => Dispatcher.Invoke(async () =>
  {
    AddLog($"휴대폰 등록 완료: {device.DeviceName}");
    ClearRegistration("등록 완료 — 코드는 한 번 사용되어 폐기됨");
    FooterStatusText.Text = $"'{device.DeviceName}' 등록이 완료되었습니다.";
    await RefreshDevicesAsync();
  });

  private void OnRelayPublished(RelayPublishedEvent relay) => Dispatcher.Invoke(() =>
  {
    var action = relay.RelayKind == "pairing" ? "페어링" : "연결";
    RelayStatusText.Text = $"{action} 중계 준비: {relay.DeviceId} → 127.0.0.1:{relay.LocalPort}";
    FooterStatusText.Text = relay.RelayKind == "pairing"
      ? "휴대폰 페어링 중계가 열렸습니다. ADB 작업 탭에서 페어링 코드를 입력하세요."
      : "휴대폰 연결 중계가 열렸습니다. ADB 작업 탭에서 ADB 연결을 누르세요.";
    AddLog(RelayStatusText.Text);
  });

  private async void MainWindow_Closing(object? sender, CancelEventArgs e)
  {
    if (_allowClose)
    {
      return;
    }

    if (_gateway.IsRunning)
    {
      var answer = MessageBox.Show(
        "Gateway가 실행 중입니다. 중계를 끄고 프로그램을 종료할까요?",
        "RoamADB Gateway 종료",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);
      if (answer != MessageBoxResult.Yes)
      {
        e.Cancel = true;
        return;
      }
    }

    e.Cancel = true;
    if (_shutdownStarted)
    {
      return;
    }

    _shutdownStarted = true;
    IsEnabled = false;
    FooterStatusText.Text = "Gateway를 정리하고 프로그램을 종료하는 중…";
    _registrationTimer.Stop();
    try
    {
      await _gateway.DisposeAsync();
    }
    catch (Exception exception)
    {
      AddLog($"종료 정리 오류: {exception.GetType().Name} — {exception.Message}");
    }
    finally
    {
      _allowClose = true;
      _ = Dispatcher.BeginInvoke(Close, DispatcherPriority.ApplicationIdle);
    }
  }

  private void AddLog(string message)
  {
    _logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
    while (_logs.Count > 100)
    {
      _logs.RemoveAt(_logs.Count - 1);
    }
  }

  private void ShowError(string title, Exception exception)
  {
    var detail = FriendlyMessage(exception);
    FooterStatusText.Text = $"오류: {detail}";
    AddLog($"오류: {detail}");
    MessageBox.Show($"{title}\n\n{detail}", "RoamADB Gateway", MessageBoxButton.OK, MessageBoxImage.Error);
  }

  private static string FriendlyMessage(Exception exception) => exception switch
  {
    FileNotFoundException => exception.Message,
    TimeoutException => exception.Message,
    SocketException => "필요한 포트를 열지 못했습니다. 다른 RoamADB/ADB 프로세스가 실행 중인지 확인하세요.",
    InvalidOperationException => exception.Message,
    ArgumentException => exception.Message,
    _ => $"{exception.GetType().Name}: {exception.Message}"
  };

  private static string KoreanCheckName(string name) => name switch
  {
    "windows" => "Windows",
    "storage" => "사용자 데이터 저장소",
    "identity" => "Gateway 인증서",
    "loopback_port" => "Gateway 포트",
    "adb" => "Android Platform-Tools",
    "tailscale" => "Tailscale",
    _ => name
  };

  private static string FormatFingerprint(string fingerprint) => string.Join(
    ':',
    Enumerable.Range(0, fingerprint.Length / 2).Select(index => fingerprint.Substring(index * 2, 2)));

  private sealed record DiagnosticRow(string Summary, string Detail);
  private sealed record DeviceRow(string DeviceId, string DeviceName, string RegisteredAtKst);
}
