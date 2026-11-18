using System.IO;
using System.Text.Json;

namespace ComputerUsageStats;

/// <summary>窗口位置与尺寸记忆，存于 D:\电脑使用时间统计数据\settings.json。</summary>
public class AppSettings
{
    public double Left { get; set; } = -1;
    public double Top { get; set; } = -1;
    public double Width { get; set; } = -1;
    public double Height { get; set; } = -1;

    public double DetailLeft { get; set; } = -1;
    public double DetailTop { get; set; } = -1;
    public double DetailWidth { get; set; } = -1;
    public double DetailHeight { get; set; } = -1;

    /// <summary>不统计锁屏界面（LockApp.exe）。</summary>
    public bool ExcludeLockApp { get; set; } = true;

    /// <summary>仅显示桌面（无窗口激活）时不统计。</summary>
    public bool SkipDesktopOnly { get; set; } = true;

    private static readonly string FilePath =
        Path.Combine(@"D:\电脑使用时间统计数据", "settings.json");

    public static AppSettings Instance { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Instance = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            // 损坏则用默认值
        }
    }

    public static void SaveDetail(double left, double top, double width, double height)
    {
        Instance.DetailLeft = left;
        Instance.DetailTop = top;
        Instance.DetailWidth = width;
        Instance.DetailHeight = height;
        Persist();
    }

    public static void Save(double left, double top, double width, double height)
    {
        Instance.Left = left;
        Instance.Top = top;
        Instance.Width = width;
        Instance.Height = height;
        Persist();
    }

    /// <summary>保存统计选项（锁屏/桌面过滤开关）。</summary>
    public static void SaveOptions()
    {
        Persist();
    }

    private static void Persist()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写失败下次再试
        }
    }
}
