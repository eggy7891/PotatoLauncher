using System.Windows;
using System.Windows.Media;
using PotatoLauncher.Core.Models;
using ThemeMode = PotatoLauncher.Core.Models.ThemeMode;

namespace PotatoLauncher.App.Services;

public sealed class ThemeService
{
    public void Apply(ThemeMode mode, string seedHex, bool reducedMotion = false)
    {
        var dark = mode switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => IsSystemDark()
        };
        var seed = ParseColor(seedHex, Color.FromRgb(240, 180, 94));
        var set = dark ? CreateDark(seed) : CreateLight(seed);
        foreach (var pair in set)
        {
            Application.Current.Resources[pair.Key] = new SolidColorBrush(pair.Value);
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            var value = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int integer && integer == 0;
        }
        catch
        {
            return true;
        }
    }

    private static Dictionary<string, Color> CreateDark(Color seed)
    {
        var surface = Mix(Color.FromRgb(18, 15, 12), seed, 0.035);
        return new Dictionary<string, Color>
        {
            ["SurfaceBrush"] = surface,
            ["SurfaceContainerBrush"] = Mix(surface, seed, 0.10),
            ["SurfaceContainerHighBrush"] = Mix(surface, seed, 0.17),
            ["SurfaceContainerHighestBrush"] = Mix(surface, seed, 0.24),
            ["OnSurfaceBrush"] = Mix(Color.FromRgb(245, 235, 224), seed, 0.04),
            ["OnSurfaceVariantBrush"] = Mix(Color.FromRgb(205, 190, 174), seed, 0.07),
            ["OutlineBrush"] = Mix(Color.FromRgb(112, 101, 91), seed, 0.12),
            ["AccentBrush"] = Lighten(seed, 0.18),
            ["OnAccentBrush"] = Mix(Color.FromRgb(32, 22, 8), seed, 0.15),
            ["AccentContainerBrush"] = Mix(surface, seed, 0.30),
            ["OnAccentContainerBrush"] = Lighten(seed, 0.72),
            ["ErrorBrush"] = Color.FromRgb(255, 180, 171),
            ["SuccessBrush"] = Color.FromRgb(157, 215, 165),
            ["HoverOverlayBrush"] = Color.FromArgb(32, 255, 255, 255)
        };
    }

    private static Dictionary<string, Color> CreateLight(Color seed)
    {
        var paper = Mix(Color.FromRgb(252, 247, 241), seed, 0.055);
        return new Dictionary<string, Color>
        {
            ["SurfaceBrush"] = paper,
            ["SurfaceContainerBrush"] = Mix(paper, seed, 0.105),
            ["SurfaceContainerHighBrush"] = Mix(paper, seed, 0.16),
            ["SurfaceContainerHighestBrush"] = Mix(paper, seed, 0.23),
            ["OnSurfaceBrush"] = Mix(Color.FromRgb(31, 27, 23), seed, 0.09),
            ["OnSurfaceVariantBrush"] = Mix(Color.FromRgb(78, 68, 59), seed, 0.10),
            ["OutlineBrush"] = Mix(Color.FromRgb(126, 114, 102), seed, 0.10),
            ["AccentBrush"] = Darken(seed, 0.22),
            ["OnAccentBrush"] = Color.FromRgb(255, 248, 238),
            ["AccentContainerBrush"] = Mix(paper, seed, 0.34),
            ["OnAccentContainerBrush"] = Darken(seed, 0.55),
            ["ErrorBrush"] = Color.FromRgb(186, 26, 26),
            ["SuccessBrush"] = Color.FromRgb(32, 110, 62),
            ["HoverOverlayBrush"] = Color.FromArgb(20, 0, 0, 0)
        };
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color Mix(Color first, Color second, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(first.R * (1 - amount) + second.R * amount),
            (byte)(first.G * (1 - amount) + second.G * amount),
            (byte)(first.B * (1 - amount) + second.B * amount));
    }

    private static Color Lighten(Color color, double amount) => Mix(color, Colors.White, amount);
    private static Color Darken(Color color, double amount) => Mix(color, Colors.Black, amount);
}
