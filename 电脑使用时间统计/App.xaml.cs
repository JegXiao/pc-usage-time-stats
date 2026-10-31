using System.Threading;
using System.Windows;

namespace ComputerUsageStats;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Local\\ComputerUsageStats_Mutex";
    private const string ShowEventName = "Local\\ComputerUsageStats_Show";

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // 已有实例在运行：通知它显示窗口，然后本进程直接退出
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { }
            Shutdown();
            return;
        }

        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            // 收到信号 = 再次点击了快捷方式：把已有窗口显示出来
            ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (_, _) => Dispatcher.BeginInvoke(() =>
                {
                    if (MainWindow is MainWindow w)
                        w.ShowFromSecondLaunch();
                }),
                null, Timeout.Infinite, false);
        }
        catch { }

        base.OnStartup(e);

        // 不用 StartupUri：确保只有第一实例才会创建窗口（第二实例在上面已 Shutdown）
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
