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
            Assert.Contains(doc.Warnings, w => w.Contains("empty", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
