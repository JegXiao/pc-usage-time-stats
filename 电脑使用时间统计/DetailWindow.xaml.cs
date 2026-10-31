using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WinForms = System.Windows.Forms;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using TextBlock = System.Windows.Controls.TextBlock;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ComputerUsageStats;

public partial class DetailWindow : Window
{
    private readonly UsageStore _store;
    private int _currentTab; // 0=今天, 1=过去7天, 2=过去30天

    private static readonly Color BarColor = Color.FromRgb(0x3D, 0x7E, 0xF5);
    private static readonly Color AvgColor = Color.FromRgb(0x3D, 0x7E, 0xF5);

    public DetailWindow(UsageStore store)
    {
        InitializeComponent();
        _store = store;

        if (AppSettings.Instance.DetailWidth > 200 && AppSettings.Instance.DetailHeight > 200)
        {
            Width = AppSettings.Instance.DetailWidth;
            Height = AppSettings.Instance.DetailHeight;
        }
        if (AppSettings.Instance.DetailLeft >= 0 && AppSettings.Instance.DetailTop >= 0)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = AppSettings.Instance.DetailLeft;
            Top = AppSettings.Instance.DetailTop;
        }

        ApplyTheme();
        SwitchTab(0);
    }

    protected override void OnClosed(EventArgs e)
    {
        AppSettings.SaveDetail(Left, Top, Width, Height);
        base.OnClosed(e);
    }

    private void ApplyTheme()
    {
        bool dark = ThemeManager.IsDarkMode();
        var res = Resources;
        byte D(bool d, byte l, byte dr) => d ? dr : l;
        void Set(string k, byte r, byte g, byte b) => res[k] = new SolidColorBrush(Color.FromRgb(r, g, b));

        Set("DetailBgBrush", D(dark, 0xF2, 0x14), D(dark, 0xF3, 0x14), D(dark, 0xF5, 0x14));
        Set("CardBrush", D(dark, 0xFF, 0x26), D(dark, 0xFF, 0x26), D(dark, 0xFF, 0x26));
        Set("TextBrush", D(dark, 0x1A, 0xF2), D(dark, 0x1A, 0xF2), D(dark, 0x1A, 0xF2));
        Set("SubTextBrush", D(dark, 0x8A, 0x9A), D(dark, 0x8A, 0x9A), D(dark, 0x8A, 0x9A));
        Set("BarRestBrush", D(dark, 0xE4, 0x3A), D(dark, 0xE6, 0x3A), D(dark, 0xEA, 0x3A));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = (HwndSource)PresentationSource.FromVisual(this)!;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SETTINGCHANGE = 0x001A;
        if (msg == WM_SETTINGCHANGE) Dispatcher.BeginInvoke(() => { ApplyTheme(); Refresh(); });
        return IntPtr.Zero;
    }

    // ---- Tab 切换 ----
    private void TabToday_Click(object sender, MouseButtonEventArgs e) => SwitchTab(0);
    private void TabWeek_Click(object sender, MouseButtonEventArgs e) => SwitchTab(1);
    private void TabMonth_Click(object sender, MouseButtonEventArgs e) => SwitchTab(2);

    private void SwitchTab(int tab)
    {
        _currentTab = tab;
        TabTodayBg.Background = (Brush)FindResource(tab == 0 ? "BarBrush" : "CardBrush");
        TabWeekBg.Background = (Brush)FindResource(tab == 1 ? "BarBrush" : "CardBrush");
        TabMonthBg.Background = (Brush)FindResource(tab == 2 ? "BarBrush" : "CardBrush");
        TabTodayText.Foreground = (Brush)FindResource(tab == 0 ? "CardBrush" : "TextBrush");
        TabWeekText.Foreground = (Brush)FindResource(tab == 1 ? "CardBrush" : "TextBrush");
        TabMonthText.Foreground = (Brush)FindResource(tab == 2 ? "CardBrush" : "TextBrush");

        Refresh();
    }

    private void Refresh()
    {
        RankingList.Children.Clear();
        ChartCanvas.Children.Clear();
        ChartLabels.Children.Clear();
        PeakHint.Text = "";

        if (_currentTab == 0) RefreshToday();
        else if (_currentTab == 1) RefreshWeek();
        else RefreshMonth();

        RefreshRanking();
    }

    // ---- 今天：纵轴固定60分钟，每10分钟一条横坐标线 ----
    private void RefreshToday()
    {
        var day = _store.GetDay(DateTime.Today.ToString("yyyy-MM-dd"));
        long total = day.Apps.Values.Sum();
        long yesterday = _store.GetDay(DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd")).Apps.Values.Sum();

        SummaryMain.Text = FormatTotal(total);
        long diff = total - yesterday;
        SummarySub.Text = diff >= 0 ? $"比昨天多用 {FormatTotal(diff)}" : $"比昨天少用 {FormatTotal(-diff)}";

        var hours = new long[24];
        foreach (var (k, v) in day.Hours) if (int.TryParse(k, out var h) && h is >= 0 and < 24) hours[h] = v;

        var tips = new List<string>();
        for (int i = 0; i < 24; i++)
            tips.Add($"{i:00}:00 - {(i + 1) % 24:00}:00  ·  {FormatTotal(hours[i])}");

        DrawTodayChart(hours.Select(v => (double)v).ToList(), tips);

        int peakHour = Array.IndexOf(hours, hours.Max());
        PeakHint.Text = $"今日最活跃时段：{peakHour:00}:00 - {(peakHour + 2) % 24:00}:00";
    }

    private void DrawTodayChart(List<double> values, List<string> tooltips)
    {
        ChartCanvas.Children.Clear();
        ChartLabels.Children.Clear();

        double width = ChartCanvas.ActualWidth;
        if (width < 10) return;
        double height = 160;

        double maxMinutes = 60; // 纵轴锁定60分钟
        double minuteHeight = height / maxMinutes;

        var gridBrush = (Brush)FindResource("BarRestBrush");
        var textBrush = (Brush)FindResource("SubTextBrush");

        // 每10分钟一条横坐标线
        for (int m = 10; m <= 60; m += 10)
        {
            double y = height - m * minuteHeight;
            var line = new Rectangle { Width = width, Height = 1, Fill = gridBrush, Opacity = 0.4 };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, y);
            ChartCanvas.Children.Add(line);

            var tb = new TextBlock { Text = $"{m}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(tb, width - 20);
            Canvas.SetTop(tb, y - 8);
            ChartLabels.Children.Add(tb);
        }

        var barBrush = new SolidColorBrush(BarColor);
        int n = values.Count;
        double slot = width / n;
        double barW = Math.Max(slot - 3, 2);

        for (int i = 0; i < n; i++)
        {
            double minutes = values[i] / 60;
            double h = Math.Min(minutes, maxMinutes) * minuteHeight;
            var bar = new Rectangle
            {
                Width = barW,
                Height = Math.Max(h, 1),
                Fill = barBrush,
                RadiusX = 2, RadiusY = 2,
                ToolTip = tooltips[i]
            };
            ToolTipService.SetInitialShowDelay(bar, 0);
            Canvas.SetLeft(bar, i * slot + (slot - barW) / 2);
            Canvas.SetTop(bar, height - Math.Max(h, 1));
            ChartCanvas.Children.Add(bar);
        }

        for (int i = 0; i < n; i += 3)
        {
            var tb = new TextBlock { Text = $"{i:00}", FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(tb, i * slot);
            Canvas.SetTop(tb, height + 4);
            ChartLabels.Children.Add(tb);
        }
    }

    // ---- 过去7天 ----
    private void RefreshWeek()
    {
        var days = _store.GetRecentDays(7);
        long total = days.Sum(d => d.TotalSeconds);
        long avg = total / 7;

        SummaryMain.Text = $"日均 {FormatTotal(avg)}";
        SummarySub.Text = $"近7天总时长 {FormatTotal(total)}";

        DrawDailyChart(days, avg);
    }

    // ---- 过去30天 ----
    private void RefreshMonth()
    {
        var days = _store.GetRecentDays(30);
        long total = days.Sum(d => d.TotalSeconds);
        long avg = total / 30;

        SummaryMain.Text = $"日均 {FormatTotal(avg)}";
        SummarySub.Text = $"30天总时长 {FormatTotal(total)}";

        DrawDailyChart(days, avg);
    }

    private void DrawDailyChart(List<(string Date, long TotalSeconds)> days, long avgSeconds)
    {
        ChartCanvas.Children.Clear();
        ChartLabels.Children.Clear();

        double width = ChartCanvas.ActualWidth;
        if (width < 10) return;
        double height = 160;

        var values = days.Select(d => (double)d.TotalSeconds).ToList();
        double max = Math.Max(values.DefaultIfEmpty(0).Max(), 1);

        var gridBrush = (Brush)FindResource("BarRestBrush");
        var textBrush = (Brush)FindResource("SubTextBrush");

        // 坐标线：每1小时一条，双数小时粗线，单数小时细线
        double maxHours = max / 3600;
        int gridCount = (int)Math.Ceiling(maxHours);
        for (int h = 1; h <= gridCount; h++)
        {
            double y = height - (h * 3600 / max) * (height - 12);
            bool even = h % 2 == 0;
            var line = new Rectangle
            {
                Width = width,
                Height = even ? 1.2 : 0.6,
                Fill = gridBrush,
                Opacity = even ? 0.6 : 0.25
            };
            Canvas.SetLeft(line, 0);
            Canvas.SetTop(line, y);
            ChartCanvas.Children.Add(line);
        }

        // 平均值蓝线
        if (avgSeconds > 0 && avgSeconds < max)
        {
            double yAvg = height - (avgSeconds / max) * (height - 12);
            var avgLine = new Rectangle
            {
                Width = width,
                Height = 1.5,
                Fill = new SolidColorBrush(AvgColor)
            };
            Canvas.SetLeft(avgLine, 0);
            Canvas.SetTop(avgLine, yAvg);
            ChartCanvas.Children.Add(avgLine);
        }

        var barBrush = new SolidColorBrush(BarColor);
        int n = values.Count;
        double slot = width / n;
        double barW = Math.Max(slot - 3, 2);
        var culture = CultureInfo.CurrentCulture;

        for (int i = 0; i < n; i++)
        {
            double h = values[i] / max * (height - 12);
            var dt = DateTime.ParseExact(days[i].Date, "yyyy-MM-dd", culture);
            string tip = $"{dt:M月d日 ddd}  ·  {FormatTotal(days[i].TotalSeconds)}";

            var bar = new Rectangle
            {
                Width = barW,
                Height = Math.Max(h, 1),
                Fill = barBrush,
                RadiusX = 2, RadiusY = 2,
                ToolTip = tip
            };
            ToolTipService.SetInitialShowDelay(bar, 0);
            Canvas.SetLeft(bar, i * slot + (slot - barW) / 2);
            Canvas.SetTop(bar, height - Math.Max(h, 1));
            ChartCanvas.Children.Add(bar);

            // 底部日期标签
            string label = dt == DateTime.Today ? "今" : dt.Day.ToString();
            var tb = new TextBlock { Text = label, FontSize = 9, Foreground = textBrush };
            Canvas.SetLeft(tb, i * slot + (slot - 20) / 2);
            Canvas.SetTop(tb, height + 4);
            ChartLabels.Children.Add(tb);
        }
    }

    // ---- 排行 ----
    private void RefreshRanking()
    {
        RankingList.Children.Clear();
        int days = _currentTab switch { 0 => 1, 1 => 7, _ => 30 };
        var apps = _store.GetAggregatedApps(days);
        if (apps.Count == 0)
        {
            RankingList.Children.Add(new TextBlock
            {
                Text = "暂无数据",
                FontSize = 13,
                Foreground = (Brush)FindResource("SubTextBrush"),
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 10)
            });
            return;
        }

        long max = apps[0].Seconds;
        foreach (var (app, secs) in apps.Take(12))
            RankingList.Children.Add(MakeRankRow(app, secs, max));
    }

    private UIElement MakeRankRow(string app, long secs, long max)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new TextBlock
        {
            Text = app.Length > 20 ? app[..18] + "..." : app,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(name, 0);

        var time = new TextBlock
        {
            Text = FormatTotal(secs),
            FontSize = 11,
            Foreground = (Brush)FindResource("SubTextBrush")
        };
        Grid.SetColumn(time, 1);

        top.Children.Add(name);
        top.Children.Add(time);

        var barBg = new Rectangle
        {
            Height = 4,
            RadiusX = 2, RadiusY = 2,
            Fill = (Brush)FindResource("BarRestBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var barFg = new Rectangle
        {
            Height = 4,
            RadiusX = 2, RadiusY = 2,
            Fill = new SolidColorBrush(BarColor),
            Width = Math.Max(22.0 * secs / max, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0)
        };

        sp.Children.Add(top);
        sp.Children.Add(barBg);
        sp.Children.Add(barFg);
        return sp;
    }

    private static string FormatTotal(long totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        if (hours > 0 && minutes > 0) return $"{hours} 小时 {minutes} 分钟";
        if (hours > 0) return $"{hours} 小时";
        return $"{minutes} 分钟";
    }
}
