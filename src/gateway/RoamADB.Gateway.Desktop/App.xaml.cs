using System.Threading;
using System.Windows;

namespace RoamADB.Gateway.Desktop;

public partial class App : Application
{
  private Mutex? _singleInstance;
  private bool _ownsSingleInstance;

  protected override void OnStartup(StartupEventArgs e)
  {
    _singleInstance = new Mutex(true, "Local\\RoamADB.Gateway.Desktop", out var createdNew);
    if (!createdNew)
    {
      MessageBox.Show(
        "RoamADB Gateway가 이미 실행 중입니다. 기존 창을 확인하세요.",
        "RoamADB Gateway",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
      Shutdown();
      return;
    }

    _ownsSingleInstance = true;

    base.OnStartup(e);
    MainWindow = new MainWindow();
    MainWindow.Show();
  }

  protected override void OnExit(ExitEventArgs e)
  {
    if (_ownsSingleInstance)
    {
      _singleInstance?.ReleaseMutex();
    }
    _singleInstance?.Dispose();
    base.OnExit(e);
  }
}
