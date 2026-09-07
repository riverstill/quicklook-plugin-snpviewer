// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.ComponentModel;
using System.Data;
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
        InitializeComponent();
        _lightTheme = ThemeHelper.IsSystemLightTheme();
        ApplyPanelTheme(_lightTheme);
        YDb.IsChecked = true;
        XLinear.IsChecked = true;
    }

    private void ApplyPanelTheme(bool light)
    {
        SetBrush("PanelBg", light ? "#FFFFFF" : "#1E1E1E");
        SetBrush("PanelFg", light ? "#202020" : "#E0E0E0");
        SetBrush("GridRowBg", light ? "#FFFFFF" : "#1E1E1E");
        SetBrush("GridAltRowBg", light ? "#F5F5F5" : "#262626");
        SetBrush("GridLine", light ? "#DDDDDD" : "#333333");
        SetBrush("GridHeaderBg", light ? "#EAEAEA" : "#2D2D30");
        SetBrush("GridHeaderFg", light ? "#202020" : "#E0E0E0");
        SetBrush("TabCheckedBg", "#007ACC");
        SetBrush("TabHoverBg", light ? "#E5E5E5" : "#3E3E42");
        SetBrush("WarningFg", light ? "#9A6B00" : "#E0A800");
    }

    private void SetBrush(string key, string hex)
    {
        if (Resources[key] is System.Windows.Media.SolidColorBrush b)
            b.Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
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
                _vm.RebuildDataGrid();
                Plot.Model = _vm.PlotModel;
                Plot.InvalidatePlot();
                SyncDataGrid();
                return;
            }

            _vm.ApplyDocument(doc!);

            // Re-apply current radio selection (the bindings may not have
            // observed the radio-button Checked events fired before the VM
            // had a document to operate on).
            YDb.IsChecked = true;
            XLinear.IsChecked = true;
            _vm.RebuildPlot(YAxisMode.Db, XAxisMode.Linear);
            _vm.RebuildDataGrid();
            SyncWarnings();

            // Push the freshly-built PlotModel to the PlotView directly.
            // Going via DataContext/Binding is racy on first show because
            // the binding may not yet have observed the initial value, and
            // subsequent rebuilds need to invalidate the visual.
            Plot.Model = _vm.PlotModel;
            Plot.InvalidatePlot();

            SyncDataGrid();
        }
        catch (Exception ex)
        {
            // Last-ditch net: never let a UI exception escape into the host.
            _vm.Summary = $"Render failed: {ex.Message}";
            try { _vm.BuildEmptyPlot(); _vm.RebuildDataGrid(); } catch { /* ignored */ }
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

    private void SyncDataGrid()
    {
        if (_vm is null) return;
        // DataGrid cannot consume a DataTable object itself; it needs the
        // DataView. Resetting ItemsSource forces column regeneration after
        // RebuildDataGrid() rebuilt the table schema via DataTable.Reset().
        DataGrid.ItemsSource = null;
        DataGrid.ItemsSource = _vm.DataTable.DefaultView;
    }

    private void SyncWarnings()
    {
        if (_vm is null) return;
        if (_vm.Warnings.Count == 0)
        {
            WarningBar.Visibility = Visibility.Collapsed;
            return;
        }
        WarningBar.Text = string.Join("   |   ", _vm.Warnings);
        WarningBar.Visibility = Visibility.Visible;
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
    public DataTable DataTable { get; } = new();

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
                $"{doc.PortCount}-port · {doc.ParamType} · {doc.DataFormat} · " +
                $"f ∈ [{fmin.ToString("G4", CultureInfo.InvariantCulture)}, " +
                $"{fmax.ToString("G4", CultureInfo.InvariantCulture)}] {_displayUnit} · " +
                $"{doc.Points.Count} samples · R = {doc.ReferenceResistance} Ω";
        }

        foreach (var w in doc.Warnings)
            Warnings.Add(w);
    }

    private void StyleAxis(Axis axis, string title)
    {
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
            Title = FileName,
            TitleColor = _theme.Fg,
            Background = _theme.Bg,
            PlotAreaBackground = _theme.Bg,
            TextColor = _theme.Fg
        };
        var xb = new LinearAxis { Position = AxisPosition.Bottom };
        var yl = new LinearAxis { Position = AxisPosition.Left };
        StyleAxis(xb, "Frequency");
        StyleAxis(yl, "Value");
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
            Title = $"{FileName} — {y} view",
            TitleColor = _theme.Fg,
            TitleFontSize = 13,
            Subtitle = Summary,
            SubtitleColor = _theme.Fg,
            Background = _theme.Bg,
            PlotAreaBackground = _theme.Bg,
            TextColor = _theme.Fg
        };
        // Series carry Titles (S11, S21, ...); an explicit legend entry is
        // required in OxyPlot 2.x (the collection is empty by default).
        // Text follows TextColor, background stays transparent.
        pm.Legends.Add(new OxyPlot.Legends.Legend());

        Axis xAxis = x == XAxisMode.Log
            ? new LogarithmicAxis { Position = AxisPosition.Bottom, Base = 10 }
            : new LinearAxis { Position = AxisPosition.Bottom };
        var yAxis = new LinearAxis { Position = AxisPosition.Left };
        StyleAxis(xAxis, $"Frequency ({_displayUnit})");
        StyleAxis(yAxis, y.ToString());
        pm.Axes.Add(xAxis);
        pm.Axes.Add(yAxis);

        int n = _doc.PortCount;
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
                    TrackerFormatString = "{0}\nf = {2:0.###} {XAxis.Title}\n{3} = {4:0.###}"
                };

                foreach (var p in _doc.Points)
                {
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

    public void RebuildDataGrid()
    {
        DataTable.Reset();
        if (_doc is null || _doc.Points.Count == 0)
            return;

        int n = _doc.PortCount;

        var freqCol = new DataColumn($"freq ({_displayUnit})", typeof(double));
        DataTable.Columns.Add(freqCol);

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                var pij = $"{_doc.ParamType}{i + 1}{j + 1}";
                DataTable.Columns.Add(new DataColumn($"{pij} re", typeof(double)));
                DataTable.Columns.Add(new DataColumn($"{pij} im", typeof(double)));
                DataTable.Columns.Add(new DataColumn($"{pij} dB", typeof(double)));
            }
        }

        const int maxRows = 1000;
        int take = Math.Min(_doc.Points.Count, maxRows);
        bool truncated = _doc.Points.Count > maxRows;

        for (int k = 0; k < take; k++)
        {
            var p = _doc.Points[k];
            var row = DataTable.NewRow();
            row[0] = ToDisplayFreq(p.Frequency);
            int col = 1;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    var c = p.S[i, j];
                    row[col++] = c.Real;
                    row[col++] = c.Imaginary;
                    row[col++] = c.Magnitude > 0 ? 20.0 * Math.Log10(c.Magnitude) : double.NaN;
                }
            }
            DataTable.Rows.Add(row);
        }

        if (truncated)
            Warnings.Add($"Preview truncated to first {maxRows} rows of {_doc.Points.Count}.");
    }
}
