using Microsoft.Win32;
using System.Windows;

namespace ComputerUsageStats;

/// <summary>读取系统深浅色并生成画刷资源。</summary>
public static class ThemeManager
{
    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(Window window, bool dark)
    {
        var res = window.Resources;
        res["CardBrush"] = new System.Windows.Media.SolidColorBrush(
            dark ? System.Windows.Media.Color.FromRgb(0x2B, 0x2B, 0x2B) : System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
        res["TextBrush"] = new System.Windows.Media.SolidColorBrush(
            dark ? System.Windows.Media.Color.FromRgb(0xF2, 0xF2, 0xF2) : System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A));
        res["SubTextBrush"] = new System.Windows.Media.SolidColorBrush(
            dark ? System.Windows.Media.Color.FromRgb(0xB0, 0xB0, 0xB0) : System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A));
        res["RingRestBrush"] = new System.Windows.Media.SolidColorBrush(
            dark ? System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55) : System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xE8));
    }
}
