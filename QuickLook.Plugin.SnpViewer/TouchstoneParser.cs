// SPDX-License-Identifier: GPL-3.0-or-later
// Touchstone file format parser.
// Supports Touchstone v1.1 (subset) and v2.x header/option/data syntax.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;

namespace QuickLook.Plugin.SnpViewer;

public static class TouchstoneParser
{
    private static readonly Regex OptionKeyRegex =
        new(@"^\s*(?:#|!)\s*([a-zA-Z_]+)\s*[:=]?\s*(.*)$",
            RegexOptions.Compiled);

    /// <summary>
    /// Parse a Touchstone file. Returns a populated document. Throws on IO errors only.
    /// </summary>
    public static TouchstoneDocument Parse(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path is null or empty.", nameof(path));
        if (!File.Exists(path))
            throw new FileNotFoundException("Touchstone file not found.", path);

        var doc = new TouchstoneDocument { FilePath = path };

        using var reader = new StreamReader(path);

        // Sniff first non-empty line to detect a Touchstone header/option line.
        string first = PeekFirstNonEmptyLine(reader);
        if (first is null)
        {
            doc.Warnings.Add("File is empty.");
            return doc;
        }

        bool looksTouchstone = LooksLikeTouchstone(first, doc);
        if (!looksTouchstone)
        {
            throw new InvalidDataException("Not a Touchstone file (no recognizable option or data line).");
        }

        // Determine initial port count from the file name (if available).
        doc.PortCount = GuessPortCountFromFileName(path);
        if (doc.PortCount == 0)
            doc.PortCount = 2; // sensible default

        int lineNo = 0;
        string? line;
        bool inNoise = false;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed[0] == '!') continue; // comment

            // v2.0 [Keyword] block markers
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                var key = trimmed.Substring(1, trimmed.Length - 2).Trim().ToLowerInvariant();
                inNoise = key == "noise";
                continue;
            }

            // Option / comment / frequency-unit line
            if (trimmed[0] == '#')
            {
                ParseOptionLine(trimmed, doc);
                continue;
            }

            if (inNoise)
            {
                ParseNoiseLine(trimmed, doc, lineNo);
                continue;
            }

            ParseDataLine(trimmed, doc, lineNo);
        }

        return doc;
    }

    private static string? PeekFirstNonEmptyLine(StreamReader reader)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null) return null;
            if (!string.IsNullOrWhiteSpace(line)) return line;
        }
    }

    private static bool LooksLikeTouchstone(string first, TouchstoneDocument doc)
    {
        var t = first.Trim();
        // v1: comment-style header "freq mag angle" (rare). v2: "# GHz S RI R 50"
        if (t.StartsWith("#") || t.StartsWith("!"))
        {
            ParseOptionLine(t, doc);
            return true;
        }

        // Otherwise expect first token to be a number (frequency)
        var firstTok = t.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return double.TryParse(firstTok, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static int GuessPortCountFromFileName(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        var m = Regex.Match(name, @"\.s(\d+)p$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n))
            return n;
        return 0;
    }

    private static void ParseOptionLine(string line, TouchstoneDocument doc)
    {
        // Strip leading '#' or '!'
        string body = line.TrimStart('#', '!').Trim();
        if (body.Length == 0) return;

        // v1.1 comments (no key) are stored as freeform comments
        if (!body.Any(c => c == ' ' || c == '\t'))
        {
            doc.HeaderComments.Add(body);
            return;
        }

        // Tokenize
        var tokens = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        int idx = 0;

        // Optional explicit keys like "freq_unit: GHz"
        while (idx < tokens.Length)
        {
            string tok = tokens[idx];

            // freq_unit
            if (MatchesFreqUnit(tok) && idx + 1 < tokens.Length && MatchesFreqUnit(tokens[idx + 1]))
            {
                doc.FrequencyUnit = ParseFreqUnit(tokens[idx + 1]);
                idx += 2;
                continue;
            }

            // param type
            if (IsParamType(tok) && idx + 1 < tokens.Length && IsParamType(tokens[idx + 1]))
            {
                doc.ParamType = ParseParamType(tokens[idx + 1]);
                idx += 2;
                continue;
            }

            // data format
            if (IsDataFormat(tok) && idx + 1 < tokens.Length && IsDataFormat(tokens[idx + 1]))
            {
                doc.DataFormat = ParseDataFormat(tokens[idx + 1]);
                idx += 2;
                continue;
            }

            // reference resistance: "R 50"
            if (tok.Equals("R", StringComparison.OrdinalIgnoreCase) && idx + 1 < tokens.Length)
            {
                if (double.TryParse(tokens[idx + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                    doc.ReferenceResistance = r;
                idx += 2;
                continue;
            }

            // unknown / comment
            doc.HeaderComments.Add(tok);
            idx++;
        }
    }

    private static bool MatchesFreqUnit(string s) =>
        s.Equals("Hz", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("kHz", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("MHz", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("GHz", StringComparison.OrdinalIgnoreCase);

    private static FrequencyUnit ParseFreqUnit(string s) => s.ToLowerInvariant() switch
    {
        "hz" => FrequencyUnit.Hz,
        "khz" => FrequencyUnit.kHz,
        "mhz" => FrequencyUnit.MHz,
        "ghz" => FrequencyUnit.GHz,
        _ => FrequencyUnit.GHz,
    };

    private static bool IsParamType(string s) =>
        s.Length == 1 && "SYZGH".Contains(s[0]);

    private static ParamType ParseParamType(string s) => char.ToUpperInvariant(s[0]) switch
    {
        'S' => ParamType.S,
        'Y' => ParamType.Y,
        'Z' => ParamType.Z,
        'G' => ParamType.G,
        'H' => ParamType.H,
        _ => ParamType.S,
    };

    private static bool IsDataFormat(string s) =>
        s.Equals("RI", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("MA", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("DB", StringComparison.OrdinalIgnoreCase);

    private static DataFormat ParseDataFormat(string s) => s.ToUpperInvariant() switch
    {
        "RI" => DataFormat.RI,
        "MA" => DataFormat.MA,
        "DB" => DataFormat.DB,
        _ => DataFormat.RI,
    };

    private static void ParseDataLine(string line, TouchstoneDocument doc, int lineNo)
    {
        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return;

        // Frequency must be a real number
        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
        {
            doc.Warnings.Add($"Line {lineNo}: expected numeric frequency, got '{tokens[0]}'. Skipped.");
            return;
        }

        int n = doc.PortCount;
        int valuesNeeded = n * n * 2;
        var values = new List<double>(tokens.Length - 1);
        for (int i = 1; i < tokens.Length; i++)
        {
            if (double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                values.Add(parsed);
            else
                doc.Warnings.Add($"Line {lineNo}: non-numeric token '{tokens[i]}' ignored.");
        }

        if (values.Count < valuesNeeded)
        {
            // In Touchstone v1, n>2 lines may be wrapped. Allow continuation.
            // For simplicity, we just warn if short.
            doc.Warnings.Add(
                $"Line {lineNo}: expected {valuesNeeded} values for {n}-port data, found {values.Count}. Parsed as partial.");
        }

        var point = new TouchstoneDataPoint
        {
            Frequency = freq,
            S = new Complex[n, n]
        };

        int v = 0;
        for (int i = 0; i < n && v + 1 < values.Count; i++)
        {
            for (int j = 0; j < n && v + 1 < values.Count; j++)
            {
                double a = values[v++];
                double b = values[v++];
                point.S[i, j] = ConvertToComplex(a, b, doc.DataFormat);
            }
        }

        doc.Points.Add(point);
    }

    private static Complex ConvertToComplex(double a, double b, DataFormat fmt)
    {
        switch (fmt)
        {
            case DataFormat.RI:
                return new Complex(a, b);
            case DataFormat.MA:
                {
                    double rad = b * Math.PI / 180.0;
                    return new Complex(a * Math.Cos(rad), a * Math.Sin(rad));
                }
            case DataFormat.DB:
                {
                    double mag = Math.Pow(10.0, a / 20.0);
                    double rad = b * Math.PI / 180.0;
                    return new Complex(mag * Math.Cos(rad), mag * Math.Sin(rad));
                }
            default:
                return new Complex(a, b);
        }
    }

    private static void ParseNoiseLine(string line, TouchstoneDocument doc, int lineNo)
    {
        var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return;

        if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var freq))
        {
            doc.Warnings.Add($"Noise line {lineNo}: bad frequency.");
            return;
        }

        if (!double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var nfDb))
        {
            doc.Warnings.Add($"Noise line {lineNo}: bad noise figure.");
            return;
        }

        var pt = new NoisePoint { Frequency = freq, NoiseFigureDb = nfDb };

        // Optional effective noise resistance (Ohms)
        if (tokens.Length >= 3 &&
            double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var rn))
        {
            pt.EffectiveNoiseResistanceOhms = rn;
        }

        // v2 noise extensions: minimum NF + source gamma (4 numbers) + normalized source resistance (1)
        // Skipped here to keep parser conservative; values stay null.

        doc.Noise.Add(pt);
    }
}
