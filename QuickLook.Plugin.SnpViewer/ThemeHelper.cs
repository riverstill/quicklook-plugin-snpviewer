// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using OxyPlot;

namespace QuickLook.Plugin.SnpViewer;

/// <summary>
/// Follows the Windows app theme (Settings > Personalization > Colors >
/// "Choose your mode") so the preview matches system light/dark mode.
/// Read once per panel: the preview window is short-lived, no live switching.
/// </summary>
internal static class ThemeHelper
{
    public static bool IsSystemLightTheme()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return Convert.ToInt32(v) != 0;
        }
        catch
        {
            return false;
        }
    }

    public static PlotTheme ForTheme(bool light) => light ? PlotTheme.Light : PlotTheme.Dark;
}

/// <summary>
/// OxyPlot colors for one UI theme. The panel XAML brushes
/// (PanelBg/PanelFg/...) are the WPF-side counterpart, swapped in
/// <see cref="SnpPanel.ApplyPanelTheme"/>.
/// </summary>
internal sealed class PlotTheme
{
    // NOTE: plain setters, not init - net462 lacks IsExternalInit.
    public OxyColor Bg { get; set; }
    public OxyColor Fg { get; set; }
    public OxyColor GridMajor { get; set; }
    public OxyColor GridMinor { get; set; }
    public OxyColor AxisLine { get; set; }
    public OxyColor LegendBg { get; set; }
    public OxyColor LegendBorder { get; set; }

    public static PlotTheme Dark { get; } = new PlotTheme
    {
        Bg = OxyColor.FromRgb(0x1E, 0x1E, 0x1E),
        Fg = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
        GridMajor = OxyColor.FromRgb(0x33, 0x33, 0x33),
        GridMinor = OxyColor.FromRgb(0x2A, 0x2A, 0x2A),
        AxisLine = OxyColor.FromRgb(0x80, 0x80, 0x80),
        LegendBg = OxyColor.FromArgb(0xE6, 0x2D, 0x2D, 0x30),
        LegendBorder = OxyColor.FromRgb(0x55, 0x55, 0x55),
    };

    public static PlotTheme Light { get; } = new PlotTheme
    {
        Bg = OxyColor.FromRgb(0xFF, 0xFF, 0xFF),
        Fg = OxyColor.FromRgb(0x20, 0x20, 0x20),
        GridMajor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
        GridMinor = OxyColor.FromRgb(0xED, 0xED, 0xED),
        AxisLine = OxyColor.FromRgb(0x80, 0x80, 0x80),
        LegendBg = OxyColor.FromArgb(0xE6, 0xFF, 0xFF, 0xFF),
        LegendBorder = OxyColor.FromRgb(0x99, 0x99, 0x99),
    };
}
