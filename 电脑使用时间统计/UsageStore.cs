using System.IO;
using System.Text.Json;

namespace ComputerUsageStats;

/// <summary>
/// 按天保存各应用使用秒数（v2 含按小时分布），JSON 持久化，只保留最近 7 天。
/// 数据位于 D:\电脑使用时间统计数据\usage.json，格式见同目录说明文件。
/// </summary>
public class UsageStore
{
    public class DayData
    {
        public Dictionary<string, long> Apps { get; set; } = new();
        public Dictionary<string, long> Hours { get; set; } = new(); // key: "0".."23"
    }

    private class Data
    {
        public int Version { get; set; } = 2;
        public Dictionary<string, DayData> Days { get; set; } = new();
    }

    private static readonly string Dir = @"D:\电脑使用时间统计数据";
    private static readonly string FilePath = Path.Combine(Dir, "usage.json");

    private Data _data = new();
    private readonly object _lock = new();
    private DateTime _lastSave = DateTime.MinValue;

    public UsageStore()
    {
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Version", out var v) && v.GetInt32() >= 2)
            {
                _data = JsonSerializer.Deserialize<Data>(json) ?? new Data();
            }
            else
            {
                // v1 迁移：{ Days: { date: { app: secs } } }
                var migrated = new Data();
                if (root.TryGetProperty("Days", out var days) && days.ValueKind == JsonValueKind.Object)
                {
                    foreach (var day in days.EnumerateObject())
                    {
                        var dd = new DayData();
                        foreach (var app in day.Value.EnumerateObject())
                            if (app.Value.TryGetInt64(out var secs))
                                dd.Apps[app.Name] = secs;
                        migrated.Days[day.Name] = dd;
                    }
                }
                _data = migrated;
            }
        }
        catch
        {
            // 文件损坏时从空数据开始
        }
    }

    public DayData GetDay(string dateKey)
    {
        lock (_lock)
        {
            return Copy(_data.Days.TryGetValue(dateKey, out var d) ? d : new DayData());
        }
    }

    /// <summary>最近 n 天（含今天）的日期键与日总量，按日期升序。</summary>
    public List<(string Date, long TotalSeconds)> GetRecentDays(int n)
    {
        lock (_lock)
        {
            var result = new List<(string, long)>();
            for (int i = n - 1; i >= 0; i--)
            {
                var key = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd");
                long total = _data.Days.TryGetValue(key, out var d) ? d.Apps.Values.Sum() : 0;
                result.Add((key, total));
            }
            return result;
        }
    }

    /// <summary>最近 n 天各应用累计秒数，降序。</summary>
    public List<(string App, long Seconds)> GetAggregatedApps(int n)
    {
        lock (_lock)
        {
            var agg = new Dictionary<string, long>();
            for (int i = n - 1; i >= 0; i--)
            {
                var key = DateTime.Today.AddDays(-i).ToString("yyyy-MM-dd");
                if (!_data.Days.TryGetValue(key, out var d)) continue;
                foreach (var (app, secs) in d.Apps)
                    agg[app] = agg.TryGetValue(app, out var v) ? v + secs : secs;
            }
            return agg.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
        }
    }

    public void AddSeconds(string dateKey, int hour, string appName, long seconds)
    {
        lock (_lock)
        {
            if (!_data.Days.TryGetValue(dateKey, out var day))
                _data.Days[dateKey] = day = new DayData();
            day.Apps[appName] = day.Apps.TryGetValue(appName, out var v) ? v + seconds : seconds;
            var hk = hour.ToString();
            day.Hours[hk] = day.Hours.TryGetValue(hk, out var h) ? h + seconds : seconds;
        }
    }

    /// <summary>节流保存：距上次保存超过 30 秒才真正写盘。</summary>
    public void SaveThrottled()
    {
        if ((DateTime.UtcNow - _lastSave).TotalSeconds < 30)
            return;
        SaveNow();
    }

    public void SaveNow()
    {
        lock (_lock)
        {
            var cutoff = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");
            foreach (var old in _data.Days.Keys.Where(k => string.Compare(k, cutoff, StringComparison.Ordinal) < 0).ToList())
                _data.Days.Remove(old);

            try
            {
                Directory.CreateDirectory(Dir);
                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(FilePath, json);
                WriteSpecDoc();
                _lastSave = DateTime.UtcNow;
            }
            catch
            {
                // 写盘失败（如被占用）时下次再试
            }
        }
    }

    private static DayData Copy(DayData d) => new()
    {
        Apps = new Dictionary<string, long>(d.Apps),
        Hours = new Dictionary<string, long>(d.Hours),
    };

    /// <summary>数据交换规范说明，随数据文件一同维护。</summary>
    private static void WriteSpecDoc()
    {
        var doc = Path.Combine(Dir, "数据格式说明.txt");
        if (File.Exists(doc)) return;
        File.WriteAllText(doc, """
            电脑使用时间统计 - 数据格式说明
            ================================

            本目录下的 usage.json 是小组件的全部数据，可直接编辑或备份。

            文件编码：UTF-8
            结构（JSON，version 2）：
            {
              "Version": 2,
              "Days": {
                "yyyy-MM-dd": {          // 每天一个键，日期为本地时间
                  "Apps": { "应用显示名": 秒数, ... },   // 各应用当日累计前台时长
                  "Hours": { "17": 秒数, ... }           // 按小时分布，key 为 0-23
                },
                ...
              }
            }

            规则：
            - 应用显示名优先取 exe 的文件描述（与小组件列表一致），否则为进程名。
            - 只保留最近 7 天，更早的日期在保存时自动清理。
            - v1 旧格式（无 Version 字段、Apps 与 Hours 未分）会在读取时自动迁移。
            - 手动修改 JSON 后重启小组件即可生效；格式错误时程序会从空数据开始，
              因此编辑前请先备份。
            """);
    }
}
