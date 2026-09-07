// SPDX-License-Identifier: GPL-3.0-or-later

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Xunit;

namespace QuickLook.Plugin.SnpViewer.Tests;

public class TouchstoneParserTests
{
    private static string SamplePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "samples", name);

    [Fact]
    public void Parse_S1P_MA_Format_DecodesMagnitudeAngle()
    {
        var path = SamplePath("termination.s1p");
        if (!File.Exists(path)) return; // skip if samples not copied

        var doc = TouchstoneParser.Parse(path);

        Assert.Equal(1, doc.PortCount);
        Assert.Equal(ParamType.S, doc.ParamType);
        Assert.Equal(DataFormat.MA, doc.DataFormat);
        Assert.Equal(FrequencyUnit.GHz, doc.FrequencyUnit);
        Assert.Equal(5, doc.Points.Count);

        var first = doc.Points[0];
        Assert.Equal(1.0, first.Frequency);
        var c = first.S[0, 0];
        double expectedReal = 0.30 * Math.Cos(-45.0 * Math.PI / 180.0);
        double expectedImag = 0.30 * Math.Sin(-45.0 * Math.PI / 180.0);
        Assert.Equal(expectedReal, c.Real, 3);
        Assert.Equal(expectedImag, c.Imaginary, 3);
    }

    [Fact]
    public void Parse_S2P_RI_Format_BuildsAllFourParameters()
    {
        var path = SamplePath("filter_2port.s2p");
        if (!File.Exists(path)) return;

        var doc = TouchstoneParser.Parse(path);

        Assert.Equal(2, doc.PortCount);
        Assert.Equal(DataFormat.RI, doc.DataFormat);
        Assert.Equal(10, doc.Points.Count);

        var first = doc.Points[0];
        Assert.Equal(4, first.S.Length); // 2x2
        Assert.Equal(0.99, first.S[0, 1].Real, 2);
        Assert.Equal(0.05, first.S[0, 1].Imaginary, 2);
        Assert.Equal(0.99, first.S[1, 0].Real, 2); // reciprocity for symmetric 2-port
    }

    [Fact]
    public void Parse_S2P_dB_WithNoise_HandlesNoiseBlock()
    {
        var path = SamplePath("amplifier_2port.s2p");
        if (!File.Exists(path)) return;

        var doc = TouchstoneParser.Parse(path);

        Assert.Equal(DataFormat.DB, doc.DataFormat);
        Assert.Equal(FrequencyUnit.MHz, doc.FrequencyUnit);
        Assert.Equal(6, doc.Points.Count);
        Assert.Equal(6, doc.Noise.Count);

        // First data row: dBS21=-0.5 → mag=10^(-0.5/20)
        var first = doc.Points[0];
        var s21 = first.S[1, 0];
        double expectedMag = Math.Pow(10.0, -0.5 / 20.0);
        Assert.Equal(expectedMag, s21.Magnitude, 3);

        // First noise row
        Assert.Equal(1000, doc.Noise[0].Frequency);
        Assert.Equal(1.5, doc.Noise[0].NoiseFigureDb);
        Assert.Equal(0.5, doc.Noise[0].EffectiveNoiseResistanceOhms);
    }

    [Fact]
    public void Parse_RejectsNonTouchstoneText()
    {
        var tmp = Path.GetTempFileName();
        File.WriteAllText(tmp, "Hello world, this is not a Touchstone file at all.");
        try
        {
            Assert.Throws<InvalidDataException>(() => TouchstoneParser.Parse(tmp));
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Parse_EmptyFile_Warns()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var doc = TouchstoneParser.Parse(tmp);
            Assert.Empty(doc.Points);
            Assert.Contains(doc.Warnings, w => w.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Parse_S4P_dB_WrappedAcrossLines_AccumulatesPointStream()
    {
        // A 4-port spawned by a wrapped-format data section: each point
        // carries 1 frequency + 32 values (4x4 pairs), but the file splits
        // them across several physical lines (a common Touchstone exporter
        // style). Regression test for negative frequencies appearing because
        // continuation lines were parsed as brand-new points.
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, string.Join("\n",
                "# MHz S DB R 50",
                "30.0 -43.1546974182 4.42637395859 -0.0155005577123 -3.5213804245 -59.6902514371 81.3233413696 -81.9710055264 -137.447784424",
                " -0.0199402879095 -3.52944231033 -47.1753616333 0.19410559535 -85.4014025601 -139.451477051 -59.6951266202 81.489692688",
                " -59.8014155301 82.5937652588 -82.5680437955 -154.644363403 -37.7304725647 2.51261568069 -0.0860866504829 -4.53315114975",
                " -84.5488940152 -146.84135437 -59.6399317654 80.7347412109 -0.0926988544979 -4.52432632446 -37.3249320984 1.78611719608",
                "42.48125 0.5 -8.45018482208 -0.00787662873808 -4.94568300247 -56.906309446 81.5283050537 -80.6370719277 -148.416854858",
                " -0.0111500711257 -4.95511960983 -48.7350845337 -23.3031349182 -80.3567069375 -143.896270752 -56.910719236 80.9483337402",
                " -56.8043120705 81.1423797607 -79.6101325356 -143.807128906 -37.4845581055 -0.103454813361 -0.0756442301987 -6.3643078804",
                " -80.2031882607 -148.123565674 -56.8799727761 81.6954498291 -0.0882341914887 -6.37493515015 -36.8609199524 -3.3355846405"));

            var doc = TouchstoneParser.Parse(tmp);

            Assert.Equal(4, doc.PortCount);
            Assert.Equal(2, doc.Points.Count);

            // Every frequency must be positive (continuation lines must NOT
            // have been misread as new points).
            Assert.All(doc.Points, p => Assert.True(p.Frequency > 0));

            // A DB value of -43.1547 dB at the first point maps to a tiny magnitude.
            var first = doc.Points[0];
            var s11db = -43.1546974182;
            var s11 = first.S[0, 0];
            Assert.Equal(Math.Pow(10.0, s11db / 20.0), s11.Magnitude, 6);

            // The very last value pair in the 32-value row feeds S[3,3].
            var s44db = -37.3249320984;
            var s44 = first.S[3, 3];
            Assert.Equal(Math.Pow(10.0, s44db / 20.0), s44.Magnitude, 6);

            Assert.Equal(42.48125, doc.Points[1].Frequency, 6);
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
