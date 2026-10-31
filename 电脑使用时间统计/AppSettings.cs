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
        try
        {
            Instance.DetailLeft = left;
            Instance.DetailTop = top;
            Instance.DetailWidth = width;
            Instance.DetailHeight = height;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写失败下次再试
        }
    }

    public static void Save(double left, double top, double width, double height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Instance = new AppSettings { Left = left, Top = top, Width = width, Height = height };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Instance, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 写失败下次再试
        }
    }
}
