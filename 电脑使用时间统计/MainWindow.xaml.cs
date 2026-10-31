using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Ellipse = System.Windows.Shapes.Ellipse;
using Path = System.Windows.Shapes.Path;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace ComputerUsageStats;

public partial class MainWindow : Window
{
    private const int TopN = 5;
    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x3D, 0x7E, 0xF5), // 蓝
        Color.FromRgb(0xF5, 0xC1, 0x3D), // 黄
        Color.FromRgb(0x4C, 0xC4, 0x5E), // 绿
        Color.FromRgb(0xF2, 0x8C, 0x28), // 橙
        Color.FromRgb(0x9B, 0x6B, 0xF0), // 紫
    };

    private readonly UsageStore _store = new();
    private readonly AppUsageTracker _tracker = new();
    private readonly DispatcherTimer _pollTimer = new();
    private readonly DispatcherTimer _uiTimer = new();
    private DateTime _lastPoll = DateTime.Now;
    private DateOnly _viewDate = DateOnly.FromDateTime(DateTime.Today);
    private WinForms.NotifyIcon? _trayIcon;
    private bool _realExit;

    public MainWindow()
    {
        InitializeComponent();
        ApplyTheme();
        InitTrayIcon();

        // 恢复上次的位置和尺寸
        AppSettings.Load();
        if (AppSettings.Instance.Width > 200 && AppSettings.Instance.Height > 200)
        {
            Width = AppSettings.Instance.Width;
            Height = AppSettings.Instance.Height;
        }
        if (AppSettings.Instance.Left >= 0 && AppSettings.Instance.Top >= 0)
        {
            Left = AppSettings.Instance.Left;
            Top = AppSettings.Instance.Top;
        }
        foreach (var thumb in ResizeOverlay.Children.OfType<System.Windows.Controls.Primitives.Thumb>())
            thumb.DragCompleted += (_, _) => SaveLayout();

        _pollTimer.Interval = TimeSpan.FromSeconds(2);
        _pollTimer.Tick += PollTick;
        _pollTimer.Start();

        _uiTimer.Interval = TimeSpan.FromSeconds(1);
        _uiTimer.Tick += UiTimerTick;
        _uiTimer.Start();

        RefreshUi();
    }

    private void SaveLayout()
    {
        AppSettings.Save(Left, Top, Width, Height);
    }

    // ---- 托盘 ----

    private void InitTrayIcon()
    {
        // 开机自启是固定行为，每次启动都强制刷新为当前 exe 路径
        AutostartHelper.SetEnabled(true);

        // 纯展示图标，无任何可操作项
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "电脑使用时间统计",
            Visible = true,
            Icon = LoadAppIcon(),
        };
    }

    /// <summary>从嵌入资源加载应用图标（与 exe 图标同源）。</summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var sri = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/app.ico"));
            if (sri != null)
                return new System.Drawing.Icon(sri.Stream);
        }
        catch { }
        return System.Drawing.SystemIcons.Application;
    }

    /// <summary>再次点击快捷方式时由第一实例调用：显示窗口。</summary>
    public void ShowFromSecondLaunch()
    {
        Show();
        Activate();
        PushToBottom();
    }

    private void ToggleWindow()
    {
        if (IsVisible)
            Hide();
        else
        {
            Show();
            Activate();
            PushToBottom();
        }
    }

    // ---- 统计 ----

    private void PollTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastPoll).TotalSeconds;
        _lastPoll = now;

        if (elapsed > 0 && elapsed < 15) // 休眠/挂起后跳过长间隔
            _tracker.Record(_store, elapsed);
        _store.SaveThrottled();

        if (DateOnly.FromDateTime(DateTime.Today) != _viewDate && _viewDate == DateOnly.FromDateTime(now))
            RefreshUi();
    }

    // ---- UI ----

    private void RefreshUi()
    {        var key = _viewDate.ToString("yyyy-MM-dd");
        var usage = _store.GetDay(key).Apps;

        long totalSeconds = usage.Values.Sum();
        (TotalTextTop.Text, TotalTextBottom.Text) = FormatTotal(totalSeconds);
        DateText.Text = _viewDate == DateOnly.FromDateTime(DateTime.Today)
            ? "今天"
            : _viewDate.ToString("M月d日", CultureInfo.CurrentCulture);
        PrevButton.IsEnabled = _viewDate > DateOnly.FromDateTime(DateTime.Today.AddDays(-6));
        NextButton.IsEnabled = _viewDate < DateOnly.FromDateTime(DateTime.Today);

        BuildAppList(usage);
        DrawRing(usage, totalSeconds);
    }

    private static (string top, string bottom) FormatTotal(long totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        return hours > 0 ? ($"{hours} 小时", $"{minutes} 分钟") : ($"{minutes} 分钟", "");
    }

    private void BuildAppList(Dictionary<string, long> usage)
    {
        AppList.Children.Clear();
        var ordered = usage.OrderByDescending(kv => kv.Value).ToList();
        long otherSeconds = 0;

        for (int i = 0; i < ordered.Count; i++)
        {
            var kv = ordered[i];
            if (i < TopN)
                AppList.Children.Add(MakeRow(new SolidColorBrush(Palette[i]), kv.Key, kv.Value));
            else
                otherSeconds += kv.Value;
        }

        if (otherSeconds > 0)
        {
            var restBrush = (Brush)FindResource("RingRestBrush");
            AppList.Children.Add(MakeRow(restBrush, "其他", otherSeconds));
        }
    }

    private UIElement MakeRow(Brush dotBrush, string name, long seconds)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse
        {
            Width = 8, Height = 8, Fill = dotBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 8, 0),
        };
        Grid.SetColumn(dot, 0);

        var nameText = new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(16, 0, 8, 0),
        };
        Grid.SetColumn(nameText, 0);

        var timeText = new TextBlock
        {
            Text = FormatDuration(seconds),
            FontSize = 11,
            Foreground = (Brush)FindResource("SubTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(timeText, 1);

        grid.Children.Add(dot);
        grid.Children.Add(nameText);
        grid.Children.Add(timeText);
        return grid;
    }

    private static string FormatDuration(long seconds)
    {
        var hours = seconds / 3600;
        var minutes = seconds % 3600 / 60;
        if (hours > 0 && minutes > 0) return $"{hours} 小时 {minutes} 分钟";
        if (hours > 0) return $"{hours} 小时";
        return $"{minutes} 分钟";
    }

    // ---- 圆环 ----

    private void DrawRing(Dictionary<string, long> usage, long totalSeconds)
    {
        RingCanvas.Children.Clear();
        const double size = 110, thickness = 13;
        var center = size / 2;
        var radius = (size - thickness) / 2;

        var restBrush = (Brush)FindResource("RingRestBrush");
        var baseRing = new Ellipse
        {
            Width = size - thickness, Height = size - thickness,
            StrokeThickness = thickness,
            Stroke = restBrush,
        };
        Canvas.SetLeft(baseRing, thickness / 2);
        Canvas.SetTop(baseRing, thickness / 2);
        RingCanvas.Children.Add(baseRing);

        if (totalSeconds <= 0) return;

        var ordered = usage.OrderByDescending(kv => kv.Value).ToList();
        long other = ordered.Skip(TopN).Sum(kv => kv.Value);
        var segments = new List<(long seconds, Brush brush)>();
        for (int i = 0; i < ordered.Count && i < TopN; i++)
            segments.Add((ordered[i].Value, new SolidColorBrush(Palette[i])));
        if (other > 0)
            segments.Add((other, restBrush));

        double angle = -90; // 从 12 点方向开始
        foreach (var (seconds, brush) in segments)
        {
            var sweep = 360.0 * seconds / totalSeconds;
            AddArc(center, radius, thickness, angle, sweep, brush);
            angle += sweep;
        }
    }

    private void AddArc(double center, double radius, double thickness, double startAngle, double sweep, Brush brush)
    {
        if (sweep <= 0.1) return;
        if (sweep >= 359.9) sweep = 359.9;

        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + sweep);
        var isLarge = sweep > 180;

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = isLarge,
            SweepDirection = SweepDirection.Clockwise,
        });
        var geo = new PathGeometry(new[] { figure });

        RingCanvas.Children.Add(new Path
        {
            Data = geo,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Flat,
        });
    }

    private static Point PointOnCircle(double center, double radius, double angleDeg)
    {
        var rad = angleDeg * Math.PI / 180;
        return new Point(center + radius * Math.Cos(rad), center + radius * Math.Sin(rad));
    }

    // ---- 主题 ----

    private void ApplyTheme()
    {
        ThemeManager.Apply(this, ThemeManager.IsDarkMode());
        RefreshUi();
    }

    // ---- 永远处于桌面层（不置顶、不抢焦点） ----

    private void UiTimerTick(object? sender, EventArgs e)
    {
        // 兜底巡检：若被任何途径最小化/隐藏，立即无焦点恢复
        if (!_dragging && (IsWindowVisible(hwndRef) == 0 || IsIconic(hwndRef)))
        {
            ShowWindow(hwndRef, SW_SHOWNOACTIVATE);
            PushToBottom();
        }
        else if (!_dragging)
        {
            PushToBottom();
        }

        // 位置/尺寸变化但未触发保存（如拖动中鼠标捕获被打断）时补存
        var s = AppSettings.Instance;
        if (!_dragging && (Math.Abs(s.Left - Left) > 0.5 || Math.Abs(s.Top - Top) > 0.5 ||
                           Math.Abs(s.Width - Width) > 0.5 || Math.Abs(s.Height - Height) > 0.5))
        {
            SaveLayout();
        }

        RefreshUi();
    }

    [DllImport("user32.dll")]
    private static extern int IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    private void PushToBottom()
    {
        if (hwndRef == IntPtr.Zero) return;
        SetWindowPos(hwndRef, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private const IntPtr HWND_BOTTOM = (IntPtr)1;
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private IntPtr hwndRef = IntPtr.Zero;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        hwndRef = new WindowInteropHelper(this).Handle;

        // 不抢焦点：点击小组件不会把它带到前台，也不会改变前台统计目标
        var ex = GetWindowLong(hwndRef, GWL_EXSTYLE);
        SetWindowLong(hwndRef, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE);

        // 启动位置夹回屏幕内，并落盘作为基线
        var pos = ClampToScreen(new Point(Left, Top));
        Left = pos.X;
        Top = pos.Y;
        SaveLayout();

        PushToBottom();

        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE)
        {
            Dispatcher.BeginInvoke(ApplyTheme);
            return IntPtr.Zero;
        }

        // Win+D / 显示桌面的两条路径都拦下：
        // 1) SC_MINIMIZE 系统命令直接吞掉
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_MINIMIZE = 0xF020;
        if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE)
        {
            handled = true;
            return IntPtr.Zero;
        }

        // 2) 若仍被最小化（部分 shell 走 ShowWindow 路径），立即无焦点恢复
        const int WM_SIZE = 0x0005;
        const int SIZE_MINIMIZED = 1;
        if (msg == WM_SIZE && wParam.ToInt32() == SIZE_MINIMIZED && !_restoring)
        {
            _restoring = true;
            ShowWindow(hwndRef, SW_SHOWNOACTIVATE);
            PushToBottom();
            _restoring = false;
            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private bool _restoring;
    private const uint SW_SHOWNOACTIVATE = 4;

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, uint cmdShow);

    // ---- 交互 ----

    private void PrevDate_Click(object sender, RoutedEventArgs e)
    {
        _viewDate = _viewDate.AddDays(-1);
        RefreshUi();
    }

    private void NextDate_Click(object sender, RoutedEventArgs e)
    {
        _viewDate = _viewDate.AddDays(1);
        RefreshUi();
    }

    private void Detail_Click(object sender, RoutedEventArgs e)
    {
        if (_detailWindow is { IsLoaded: true })
        {
            _detailWindow.Activate();
            return;
        }
        _detailWindow = new DetailWindow(_store) { Owner = this };
        _detailWindow.Show();
    }

    private DetailWindow? _detailWindow;

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1) return;
        if (!GetCursorPos(out var cursor)) return;
        _dragCursorStart = (cursor.X, cursor.Y);
        _dragWindowStart = new Point(Left, Top);
        _dragging = true;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || !GetCursorPos(out var cursor)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var x = _dragWindowStart.X + (cursor.X - _dragCursorStart.x) / dpi.DpiScaleX;
        var y = _dragWindowStart.Y + (cursor.Y - _dragCursorStart.y) / dpi.DpiScaleY;

        var pos = ClampToScreen(new Point(x, y));
        Left = pos.X;
        Top = pos.Y;
    }

    /// <summary>保证整个窗口完全在当前屏幕工作区内。</summary>
    private Point ClampToScreen(Point p)
    {
        var area = GetWorkAreaDip();
        p.X = Math.Min(Math.Max(p.X, area.X), area.Right - Width);
        p.Y = Math.Min(Math.Max(p.Y, area.Y), area.Bottom - Height);
        return p;
    }

    /// <summary>当前显示器的工作区（去掉任务栏），物理像素换算为 DIP。</summary>
    private Rect GetWorkAreaDip()
    {
        var screen = hwndRef != IntPtr.Zero
            ? WinForms.Screen.FromHandle(hwndRef)
            : WinForms.Screen.PrimaryScreen;
        var wa = screen!.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            wa.X / dpi.DpiScaleX,
            wa.Y / dpi.DpiScaleY,
            wa.Width / dpi.DpiScaleX,
            wa.Height / dpi.DpiScaleY);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        SaveLayout();
    }

    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);

    private (int x, int y) _dragCursorStart;
    private Point _dragWindowStart;
    private bool _dragging;

    // ---- 大小调节模式 ----

    private const double MinWidthDip = 280, MinHeightDip = 360;

    private void ResizeMode_Click(object sender, RoutedEventArgs e)
    {
        ResizeOverlay.Visibility = ResizeOverlay.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void Grip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.Thumb thumb) return;
        var dirs = (string)thumb.Tag;
        var dpi = VisualTreeHelper.GetDpi(this);

        var area = GetWorkAreaDip();
        double right = Left + Width, bottom = Top + Height;
        double newWidth = Width, newHeight = Height, newLeft = Left, newTop = Top;

        if (dirs.Contains('W'))
        {
            newLeft = Math.Min(Left + e.HorizontalChange / dpi.DpiScaleX, right - MinWidthDip);
            newWidth = right - newLeft;
        }
        if (dirs.Contains('E'))
            newWidth = Math.Max(MinWidthDip, Width + e.HorizontalChange / dpi.DpiScaleX);
        if (dirs.Contains('N'))
        {
            newTop = Math.Min(Top + e.VerticalChange / dpi.DpiScaleY, bottom - MinHeightDip);
            newHeight = bottom - newTop;
        }
        if (dirs.Contains('S'))
            newHeight = Math.Max(MinHeightDip, Height + e.VerticalChange / dpi.DpiScaleY);

        // 尺寸和位置都限制在工作区内
        newWidth = Math.Min(newWidth, area.Width);
        newHeight = Math.Min(newHeight, area.Height);
        newLeft = Math.Min(Math.Max(newLeft, area.X), area.Right - newWidth);
        newTop = Math.Min(Math.Max(newTop, area.Y), area.Bottom - newHeight);

        Left = newLeft; Top = newTop; Width = newWidth; Height = newHeight;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_realExit)
        {
            // 点关闭 = 最小化到托盘，统计继续
            e.Cancel = true;
            Hide();
            return;
        }

        _store.SaveNow();
        _trayIcon?.Dispose();
        base.OnClosing(e);
    }
}
