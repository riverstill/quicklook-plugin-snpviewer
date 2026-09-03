// SPDX-License-Identifier: GPL-3.0-or-later
// Touchstone file format data model.
// Spec references:
//   - Touchstone File Format Specification Version 2.1 (IBIS Open Forum, 2024-01-26)
//   - Touchstone File Format Specification Version 1.1 (legacy)

using System.Collections.Generic;
using System.Numerics;

namespace QuickLook.Plugin.SnpViewer;

public enum FrequencyUnit
{
    Hz,
    kHz,
    MHz,
    GHz
}

public enum ParamType
{
    S, // Scattering (default)
    Y, // Admittance
    Z, // Impedance
    G, // Hybrid-G
    H  // Hybrid-H
}

public enum DataFormat
{
    RI, // Real / Imaginary (default)
    MA, // Magnitude / Angle (degrees)
    DB  // Decibels / Angle (degrees)
}

/// <summary>
/// One sampled data point from a Touchstone file.
/// </summary>
public sealed class TouchstoneDataPoint
{
    public double Frequency { get; set; }
    public Complex[,] S { get; set; } = new Complex[0, 0];

    public double SijDb(int i, int j)
    {
        var c = S[i, j];
        double mag = c.Magnitude;
        return mag <= 0 ? double.NegativeInfinity : 20.0 * System.Math.Log10(mag);
    }

    public double SijMag(int i, int j) => S[i, j].Magnitude;
}

/// <summary>
/// Optional noise data point, one per port per frequency.
/// </summary>
public sealed class NoisePoint
{
    public double Frequency { get; set; }
    public double NoiseFigureDb { get; set; }
    public double? EffectiveNoiseResistanceOhms { get; set; }
    public double? MinimumNoiseFigureDb { get; set; }
    public Complex? OptimumSourceGamma { get; set; }
    public double? NormalizedSourceResistance { get; set; }
}

/// <summary>
/// In-memory representation of a Touchstone file.
/// </summary>
public sealed class TouchstoneDocument
{
    public string FilePath { get; set; } = string.Empty;
    public int PortCount { get; set; }
    public ParamType ParamType { get; set; } = ParamType.S;
    public DataFormat DataFormat { get; set; } = DataFormat.RI;
    public FrequencyUnit FrequencyUnit { get; set; } = FrequencyUnit.GHz;
    public double ReferenceResistance { get; set; } = 50.0;
    public List<TouchstoneDataPoint> Points { get; } = new();
    public List<NoisePoint> Noise { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> HeaderComments { get; } = new();
}
