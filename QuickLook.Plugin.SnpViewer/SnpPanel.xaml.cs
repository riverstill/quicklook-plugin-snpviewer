// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace QuickLook.Plugin.SnpViewer;

public partial class SnpPanel : UserControl
{
    public SnpPanel()
    {
        InitializeComponent();
        YDb.IsChecked = true;
        XLinear.IsChecked = true;
    }

    private SnpViewModel? _vm;

    public async void LoadFile(string path)
    {
        _vm = new SnpViewModel(path);
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
        _vm?.RebuildPlot(YAxisMode.Db, _vm.CurrentX);

    private void YMagnitude_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(YAxisMode.Magnitude, _vm.CurrentX);

    private void YReal_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(YAxisMode.Real, _vm.CurrentX);

    private void YImag_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(YAxisMode.Imaginary, _vm.CurrentX);

    private void YPhase_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(YAxisMode.Phase, _vm.CurrentX);

    private void XLinear_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(_vm.CurrentY, XAxisMode.Linear);

    private void XLog_OnChecked(object sender, RoutedEventArgs e) =>
        _vm?.RebuildPlot(_vm.CurrentY, XAxisMode.Log);
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

public class SnpViewModel
{
    private readonly string _path;
    public string FileName { get; }
    public string Summary { get; set; } = string.Empty;
    public System.Collections.ObjectModel.ObservableCollection<string> Warnings { get; } = new();
    public DataTable DataTable { get; } = new();

    public PlotModel PlotModel { get; private set; } = new();
    public YAxisMode CurrentY { get; private set; } = YAxisMode.Db;
    public XAxisMode CurrentX { get; private set; } = XAxisMode.Linear;

    private TouchstoneDocument? _doc;

    public SnpViewModel(string path)
    {
        _path = path;
        FileName = Path.GetFileName(path);
    }

    public void ApplyDocument(TouchstoneDocument doc)
    {
        _doc = doc;

        if (doc.Points.Count == 0)
        {
            Summary = "No data points parsed.";
        }
        else
        {
            var fmin = doc.Points.First().Frequency;
            var fmax = doc.Points.Last().Frequency;
            Summary =
                $"{doc.PortCount}-port · {doc.ParamType} · {doc.DataFormat} · " +
                $"f ∈ [{fmin.ToString("G4", CultureInfo.InvariantCulture)}, " +
                $"{fmax.ToString("G4", CultureInfo.InvariantCulture)}] {doc.FrequencyUnit} · " +
                $"{doc.Points.Count} samples · R = {doc.ReferenceResistance} Ω";
        }

        foreach (var w in doc.Warnings)
            Warnings.Add(w);
    }

    public void BuildEmptyPlot()
    {
        var pm = new PlotModel
        {
            Title = FileName,
            TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
            PlotAreaBackground = OxyColor.FromRgb(0xFF, 0xFF, 0xFF),
            TextColor = OxyColor.FromRgb(0x20, 0x20, 0x20)
        };
        pm.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Frequency" });
        pm.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Value" });
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
            TitleColor = OxyColor.FromRgb(0xE0, 0xE0, 0xE0),
            PlotAreaBackground = OxyColor.FromRgb(0xFF, 0xFF, 0xFF),
            TextColor = OxyColor.FromRgb(0x20, 0x20, 0x20)
        };

        pm.Axes.Add(x == XAxisMode.Log
            ? new LogarithmicAxis { Position = AxisPosition.Bottom, Title = $"Frequency ({_doc.FrequencyUnit})", Base = 10 }
            : new LinearAxis { Position = AxisPosition.Bottom, Title = $"Frequency ({_doc.FrequencyUnit})" });

        pm.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = y.ToString() });

        int n = _doc.PortCount;
        var palette = new[]
        {
            OxyColor.FromRgb(0x1F, 0x77, 0xB4),
            OxyColor.FromRgb(0xD6, 0x27, 0x28),
            OxyColor.FromRgb(0x2C, 0xA0, 0x2C),
            OxyColor.FromRgb(0xFF, 0x7F, 0x0E),
            OxyColor.FromRgb(0x94, 0x67, 0xBD),
            OxyColor.FromRgb(0x8C, 0x56, 0x4B),
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
                    MarkerType = MarkerType.None
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
                    series.Points.Add(new DataPoint(p.Frequency, yv));
                }

                pm.Series.Add(series);
                colorIdx++;
            }
        }

        PlotModel = pm;
    }

    public void RebuildDataGrid()
    {
        DataTable.Reset();
        if (_doc is null || _doc.Points.Count == 0)
            return;

        int n = _doc.PortCount;

        var freqCol = new DataColumn($"freq ({_doc.FrequencyUnit})", typeof(double));
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
            row[0] = p.Frequency;
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
