// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace QuickLook.Plugin.SnpViewer;

public partial class SnpPanel : UserControl
{
    private readonly bool _lightTheme;

    public SnpPanel()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            // Stop the panic: if BAML fails, the host gets "Failed to preview"
            // and the stack is lost. Recover by disabling the UI pieces that
            // depend on named elements we no longer have (they are null-safe)
            // and keep going - the error will surface via LoadFile's Summary.
            Console.WriteLine("SnpPanel.InitializeComponent failed: " + ex);
        }

        try
        {
            _lightTheme = ThemeHelper.IsSystemLightTheme();
            ApplyPanelTheme(_lightTheme);
        }
        catch (Exception ex)
        {
            Console.WriteLine("SnpPanel theme init failed: " + ex);
        }

        try
        {
            YDb.IsChecked = true;
            XLinear.IsChecked = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("SnpPanel radio init failed: " + ex);
        }

        try
        {
            ApplyLanguage();
        }
        catch (Exception ex)
        {
            Console.WriteLine("SnpPanel language init failed: " + ex);
        }
    }

    private void ApplyLanguage()
    {
        YAxisLabel.Text = Lang.Pick("Y轴：", "Y-axis:");
        XAxisLabel.Text = Lang.Pick("X轴：", "X-axis:");
        // dB is a unit symbol, identical in both languages.
        YMagnitude.Content = Lang.Pick("幅值", "Magnitude");
        YReal.Content = Lang.Pick("实部", "Real");
        YImag.Content = Lang.Pick("虚部", "Imaginary");
        YPhase.Content = Lang.Pick("相位 (°)", "Phase (°)");
        XLinear.Content = Lang.Pick("线性", "Linear");
        XLog.Content = Lang.Pick("对数", "Log");
    }

    private void ApplyPanelTheme(bool light)
    {
        try
        {
            SetBrush("PanelBg", light ? "#FFFFFF" : "#1E1E1E");
            SetBrush("PanelFg", light ? "#202020" : "#E0E0E0");
        SetBrush("GridRowBg", light ? "#FFFFFF" : "#1E1E1E");
        SetBrush("GridAltRowBg", light ? "#F5F5F5" : "#262626");
        SetBrush("GridLine", light ? "#DDDDDD" : "#333333");
        SetBrush("TabCheckedBg", "#007ACC");
        SetBrush("TabBg", light ? "#EFEFEF" : "#2A2A2E");
            SetBrush("TabHoverBg", light ? "#E5E5E5" : "#3E3E42");
            SetBrush("WarningFg", light ? "#9A6B00" : "#E0A800");
            SetBrush("TrackerBg", light ? "#FFFFE0" : "#2D2D30");
            SetBrush("TrackerBorder", light ? "#000000" : "#808080");
            SetBrush("TrackerFg", light ? "#000000" : "#E0E0E0");
            SetBrush("TrackerCrosshair", light ? "#808080" : "#808080");
        }
        catch (Exception ex)
        {
            Console.WriteLine("SnpPanel theme brush setup failed: " + ex);
        }
    }

    private void SetBrush(string key, string hex)
    {
        // Never mutate the existing brush: WPF freezes Freezables once they
        // have been used by the rendering layer, and setting Color on a frozen
        // brush throws InvalidOperationException ("object is in a read-only
        // state") - this killed the whole preview before. Replace the entry
        // instead; every consumer uses {DynamicResource} so they re-resolve.
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        Resources[key] = new System.Windows.Media.SolidColorBrush(color);
    }

    private SnpViewModel? _vm;

    public async void LoadFile(string path)
    {
        _vm = new SnpViewModel(path, _lightTheme);
        DataContext = _vm;

        YDb.IsChecked = true;
        XLinear.IsChecked = true;

        TouchstoneDocument? doc = null;
        Exception? parseError = null;
        try
        {
            doc = await Task.Run(() => TouchstoneParser.Parse(path));
        }
        catch (Exception ex)
        {
            parseError = ex;
        }

        try
        {
            if (parseError is not null)
            {
                _vm.Summary = $"Failed to parse: {parseError.Message}";
                _vm.BuildEmptyPlot();
                Plot.Model = _vm.PlotModel;
                Plot.InvalidatePlot();
                SyncHeader();
                return;
            }

            _vm.ApplyDocument(doc!);

            // Re-apply current radio selection (the bindings may not have
            // observed the radio-button Checked events fired before the VM
            // had a document to operate on).
            YDb.IsChecked = true;
            XLinear.IsChecked = true;
            _vm.RebuildPlot(YAxisMode.Db, XAxisMode.Linear);
            SyncWarnings();
            SyncHeader();

            // Push the freshly-built PlotModel to the PlotView directly.
            // Going via DataContext/Binding is racy on first show because
            // the binding may not yet have observed the initial value, and
            // subsequent rebuilds need to invalidate the visual.
            Plot.Model = _vm.PlotModel;
            Plot.InvalidatePlot();
        }
        catch (Exception ex)
        {
            // Last-ditch net: never let a UI exception escape into the host.
            _vm.Summary = $"Render failed: {ex.Message}";
            try { _vm.BuildEmptyPlot(); SyncHeader(); } catch { /* ignored */ }
        }
    }

    private void SnpPanel_OnLoaded(object sender, RoutedEventArgs e)
    {
    }

    private void YDb_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(YAxisMode.Db, _vm.CurrentX));

    private void YMagnitude_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(YAxisMode.Magnitude, _vm.CurrentX));

    private void YReal_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(YAxisMode.Real, _vm.CurrentX));

    private void YImag_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(YAxisMode.Imaginary, _vm.CurrentX));

    private void YPhase_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(YAxisMode.Phase, _vm.CurrentX));

    private void XLinear_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(_vm.CurrentY, XAxisMode.Linear));

    private void XLog_OnChecked(object sender, RoutedEventArgs e) =>
        SyncPlot(() => _vm?.RebuildPlot(_vm.CurrentY, XAxisMode.Log));

    private void SyncPlot(Action? rebuild)
    {
        if (rebuild is null || _vm is null) return;
        rebuild();
        Plot.Model = _vm.PlotModel;
        Plot.InvalidatePlot();
    }

    private void SyncWarnings()
    {
        if (_vm is null) return;
        if (_vm.Warnings.Count == 0)
        {
            WarningBar.Visibility = Visibility.Collapsed;
            return;
        }
        const int maxShown = 4;
        var shown = _vm.Warnings.Take(maxShown).ToList();
        var text = string.Join("   |   ", shown);
        if (_vm.Warnings.Count > maxShown)
            text += $"   |   (+{_vm.Warnings.Count - maxShown} more)";
        WarningBar.Text = text;
        WarningBar.Visibility = Visibility.Visible;
    }

    private void SyncHeader()
    {
        if (_vm is null) return;
        HeaderSubtitle.Text = _vm.Summary;
        FilePillText.Text = _vm.FileName;
    }
}

public enum YAxisMode
{
    Db,
    Magnitude,
    Real,
    Imaginary,
    Phase
}

public enum XAxisMode
{
    Linear,
    Log
}

public class SnpViewModel : INotifyPropertyChanged
{
    private readonly string _path;
    public string FileName { get; }
    public string Summary { get; set; } = string.Empty;
    public System.Collections.ObjectModel.ObservableCollection<string> Warnings { get; } = new();

    private PlotModel _plotModel = new();
    public PlotModel PlotModel
    {
        get => _plotModel;
        private set
        {
            if (ReferenceEquals(_plotModel, value)) return;
            _plotModel = value;
            OnPropertyChanged();
        }
    }

    public YAxisMode CurrentY { get; private set; } = YAxisMode.Db;
    public XAxisMode CurrentX { get; private set; } = XAxisMode.Linear;

    private TouchstoneDocument? _doc;
    private readonly PlotTheme _theme;

    public SnpViewModel(string path, bool lightTheme = false)
    {
        _path = path;
        FileName = Path.GetFileName(path);
        _theme = ThemeHelper.ForTheme(lightTheme);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Display unit for the frequency axis / table: picked from the data so
    // a 0..20 GHz sweep reads "GHz" while a 0..500 MHz sweep reads "MHz".
    private FrequencyUnit _displayUnit = FrequencyUnit.GHz;
    private double _displayFactor = 1.0;
    private double ToDisplayFreq(double raw) => raw * _displayFactor;

    private static double UnitToHz(FrequencyUnit u) => u switch
    {
        FrequencyUnit.Hz => 1.0,
        FrequencyUnit.kHz => 1e3,
        FrequencyUnit.MHz => 1e6,
        FrequencyUnit.GHz => 1e9,
        _ => 1.0,
    };

    private void UpdateDisplayUnit()
    {
        if (_doc is null || _doc.Points.Count == 0)
        {
            _displayUnit = _doc?.FrequencyUnit ?? FrequencyUnit.GHz;
            _displayFactor = 1.0;
            return;
        }

        double maxHz = _doc.Points.Max(p => p.Frequency) * UnitToHz(_doc.FrequencyUnit);
        _displayUnit = maxHz >= 1e9 ? FrequencyUnit.GHz
            : maxHz >= 1e6 ? FrequencyUnit.MHz
            : maxHz >= 1e3 ? FrequencyUnit.kHz
            : FrequencyUnit.Hz;
        _displayFactor = UnitToHz(_doc.FrequencyUnit) / UnitToHz(_displayUnit);
    }

    public void ApplyDocument(TouchstoneDocument doc)
    {
        _doc = doc;
        UpdateDisplayUnit();

        if (doc.Points.Count == 0)
        {
            Summary = "No data points parsed.";
        }
        else
        {
            var fmin = ToDisplayFreq(doc.Points.First().Frequency);
            var fmax = ToDisplayFreq(doc.Points.Last().Frequency);
            Summary =
                Lang.Pick($"{doc.PortCount}端口", $"{doc.PortCount}-port") + " · " +
                $"{doc.ParamType} · {doc.DataFormat} · " +
                $"f ∈ [{fmin.ToString("G4", CultureInfo.InvariantCulture)}, " +
                $"{fmax.ToString("G4", CultureInfo.InvariantCulture)}] {_displayUnit} · " +
                Lang.Pick($"{doc.Points.Count} 点", $"{doc.Points.Count} samples") + " · " +
                $"R = {doc.ReferenceResistance} Ω";
        }

        foreach (var w in doc.Warnings)
            Warnings.Add(w);
    }

    private static string YAxisTitle(YAxisMode y) => y switch
    {
        YAxisMode.Db => Lang.Pick("幅值 (dB)", "Magnitude (dB)"),
        YAxisMode.Magnitude => Lang.Pick("幅值", "Magnitude"),
        YAxisMode.Real => Lang.Pick("实部", "Real"),
        YAxisMode.Imaginary => Lang.Pick("虚部", "Imaginary"),
        YAxisMode.Phase => Lang.Pick("相位 (°)", "Phase (°)"),
        _ => y.ToString()
    };

    // Pin the coordinate area's right edge to the container: the right side
    // carries no axis/tick/title, so both the outer padding and the margin
    // can go to 0 there while left/top/bottom stay automatic.
    private static void PinPlotAreaRight(PlotModel pm)
    {
        pm.Padding = new OxyThickness(8, 8, 0, 8);
        pm.PlotMargins = new OxyThickness(double.NaN, double.NaN, 0, double.NaN);
    }

    private void StyleAxis(Axis axis, string title)    {
        axis.Title = title;
        axis.TitleColor = _theme.Fg;
        axis.TextColor = _theme.Fg;
        axis.AxislineColor = _theme.AxisLine;
        axis.TicklineColor = _theme.AxisLine;
        axis.MajorGridlineColor = _theme.GridMajor;
        axis.MajorGridlineStyle = LineStyle.Solid;
        axis.MinorGridlineColor = _theme.GridMinor;
        axis.MinorGridlineStyle = LineStyle.Solid;
    }

    public void BuildEmptyPlot()
    {
        var pm = new PlotModel
        {
            // No plot-level title: the XAML header carries the title and
            // the summary line instead.
            Background = _theme.Bg,
            PlotAreaBackground = _theme.Bg,
            TextColor = _theme.Fg
        };
        PinPlotAreaRight(pm);
        var xb = new LinearAxis { Position = AxisPosition.Bottom };
        var yl = new LinearAxis { Position = AxisPosition.Left };
        StyleAxis(xb, Lang.Pick("频率", "Frequency"));
        StyleAxis(yl, Lang.Pick("数值", "Value"));
        pm.Axes.Add(xb);
        pm.Axes.Add(yl);
        PlotModel = pm;
    }

    public void RebuildPlot(YAxisMode y, XAxisMode x)
    {
        CurrentY = y;
        CurrentX = x;
        if (_doc is null || _doc.Points.Count == 0)
        {
            BuildEmptyPlot();
            return;
        }

        var pm = new PlotModel
        {
            // No plot-level title: the XAML header carries "S-Parameters"
            // plus the summary line instead.
            Background = _theme.Bg,
            PlotAreaBackground = _theme.Bg,
            TextColor = _theme.Fg
        };
        PinPlotAreaRight(pm);
        // Boxed legend, top-right inside the plot area. The Legends
        // collection is empty by default in OxyPlot 2.x, so without this
        // nothing renders even though series carry Titles (S11, S21, ...).
        pm.Legends.Add(new OxyPlot.Legends.Legend
        {
            LegendPosition = OxyPlot.Legends.LegendPosition.RightBottom,
            LegendPlacement = OxyPlot.Legends.LegendPlacement.Inside,
            LegendOrientation = OxyPlot.Legends.LegendOrientation.Vertical,
            LegendBackground = _theme.LegendBg,
            LegendBorder = _theme.LegendBorder,
            LegendBorderThickness = 1,
            LegendTextColor = _theme.Fg,
            LegendFontSize = 11,
            LegendSymbolLength = 24,
            LegendPadding = 8,
            LegendMargin = 8,
            // Matrix layout: with Vertical orientation, items wrap into a new
            // column once they exceed the available height. Inside legends get
            // min(plotArea, MaxHeight) minus 2xMargin, then minus padding, so
            // budget 2xMargin + 2xPadding + N x 18px rows (11pt rows measure
            // ~15px; verified against 2.1.2 Legend.Rendering wrap logic).
            // An N-port file's N^2 series then form an N x N grid.
            LegendMaxHeight = 32 + _doc.PortCount * 18,
            LegendColumnSpacing = 12,
        });

        Axis xAxis = x == XAxisMode.Log
            ? new LogarithmicAxis { Position = AxisPosition.Bottom, Base = 10 }
            : new LinearAxis { Position = AxisPosition.Bottom };
        var yAxis = new LinearAxis { Position = AxisPosition.Left };
        StyleAxis(xAxis, Lang.Pick($"频率 ({_displayUnit})", $"Frequency ({_displayUnit})"));
        StyleAxis(yAxis, YAxisTitle(y));
        // Push the Y title left, away from the tick labels (default gap is 4).
        yAxis.AxisTitleDistance = 14;
        pm.Axes.Add(xAxis);
        pm.Axes.Add(yAxis);

        int n = _doc.PortCount;
        // Big files with tens of thousands of points freeze OxyPlot. Decimate
        // the handful of points we actually plot while keeping the DataGrid's
        // separate 1000-row cap.
        int plotEvery = Math.Max(1, (int)Math.Ceiling((double)_doc.Points.Count / 4000));
        // Brightened for the dark background (matplotlib "tab" set with the
        // low-contrast brown swapped for yellow).
        var palette = new[]
        {
            OxyColor.FromRgb(0x1F, 0x77, 0xB4),
            OxyColor.FromRgb(0xD6, 0x27, 0x28),
            OxyColor.FromRgb(0x2C, 0xA0, 0x2C),
            OxyColor.FromRgb(0xFF, 0x7F, 0x0E),
            OxyColor.FromRgb(0x94, 0x67, 0xBD),
            OxyColor.FromRgb(0xBC, 0xBD, 0x22),
            OxyColor.FromRgb(0xE3, 0x77, 0xC2),
            OxyColor.FromRgb(0x17, 0xBE, 0xCF),
        };
        int colorIdx = 0;

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                var series = new LineSeries
                {
                    Title = $"{_doc.ParamType}{i + 1}{j + 1}",
                    Color = palette[colorIdx % palette.Length],
                    StrokeThickness = 1.2,
                    MarkerType = MarkerType.None,
                    TrackerFormatString = "{0}\n{2:0.###} {1}\n{4:0.###} {3}"
                };

                for (int k = 0; k < _doc.Points.Count; k += plotEvery)
                {
                    var p = _doc.Points[k];
                    var c = p.S[i, j];
                    double yv = y switch
                    {
                        YAxisMode.Db => c.Magnitude > 0 ? 20.0 * Math.Log10(c.Magnitude) : double.NaN,
                        YAxisMode.Magnitude => c.Magnitude,
                        YAxisMode.Real => c.Real,
                        YAxisMode.Imaginary => c.Imaginary,
                        YAxisMode.Phase => c.Magnitude > 0 ? c.Phase * 180.0 / Math.PI : double.NaN,
                        _ => c.Real
                    };
                    series.Points.Add(new DataPoint(ToDisplayFreq(p.Frequency), yv));
                }

                pm.Series.Add(series);
                colorIdx++;
            }
        }

        // Lock the dB scale to a sensible default (S11 typically -20..0 dB,
        // S21 in passband near 0 dB). Without this OxyPlot may pick an
        // autoscale that hides the data when log of small magnitudes yields
        // very negative values.
        if (y == YAxisMode.Db)
        {
            var ya = pm.Axes.OfType<LinearAxis>().First(a => a.Position == AxisPosition.Left);
            ya.Minimum = -60;
            ya.Maximum = 5;
            ya.MajorStep = 10;
        }

        PlotModel = pm;
    }
}
