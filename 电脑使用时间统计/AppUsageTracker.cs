using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ComputerUsageStats;

/// <summary>前台窗口轮询统计：记录当前前台应用，把轮询间隔累加到它的使用时长。</summary>
public class AppUsageTracker
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    /// <summary>前台应用变化时触发（进程名, 显示名, 窗口标题）。</summary>
    public event Action<string, string, string>? ForegroundAppChanged;

    private readonly Dictionary<int, string> _displayNames = new();

    /// <summary>轮询一次：把 interval 秒计入当前前台应用；跨天时归档到正确的日期。</summary>
    public void Record(UsageStore store, double intervalSeconds)
    {
        var now = DateTime.Now;
        var (pid, procName, display) = GetForegroundApp();
        if (procName == null)
            return;

        // 前台是自己的进程时不计入（防御：正常情况下小组件不抢焦点）
        if (pid == Environment.ProcessId)
            return;

        store.AddSeconds(now.ToString("yyyy-MM-dd"), now.Hour, display, (long)Math.Round(intervalSeconds));
    }

    private (uint pid, string? procName, string displayName) GetForegroundApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return (0, null, "");

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
            return (0, null, "");

        string procName;
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            procName = proc.ProcessName;
        }
        catch
        {
            // 进程刚退出
            return (0, null, "");
        }

        if (!_displayNames.TryGetValue((int)pid, out var display))
        {
            display = GetFileDescription((int)pid) ?? procName;
            _displayNames[(int)pid] = display;
        }

        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, 256);
        ForegroundAppChanged?.Invoke(procName, display, title.ToString());
        return (pid, procName, display);
    }

    private static string? GetFileDescription(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            var path = proc.MainModule?.FileName;
            if (path == null || !System.IO.File.Exists(path))
                return null;
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileDescription) ? null : info.FileDescription;
        }
        catch
        {
            return null;
        }
    }
}
