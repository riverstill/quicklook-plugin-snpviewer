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

        // Determine initial port count from the file name (if available).
        doc.PortCount = GuessPortCountFromFileName(path);
        if (doc.PortCount == 0)
            doc.PortCount = 2; // sensible default

        // Read entire file so we can sniff the first non-empty line without
        // losing it from the main parse loop.
        var allLines = File.ReadAllLines(path);
        if (allLines.Length == 0)
        {
            doc.Warnings.Add("File is empty.");
            return doc;
        }

        int firstIdx = -1;
        for (int i = 0; i < allLines.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(allLines[i])) { firstIdx = i; break; }
        }
        if (firstIdx < 0)
        {
            doc.Warnings.Add("File is empty.");
            return doc;
        }

        string first = allLines[firstIdx].Trim();
        if (!LooksLikeTouchstone(first))
        {
            throw new InvalidDataException("Not a Touchstone file (no recognizable option or data line).");
        }

        // Multi-line data support: Touchstone allows one point (frequency +
        // n^2 pairs) to wrap across several physical lines, most commonly for
        // n > 2. We buffer values from continuation lines until the point is
        // complete, then commit it. A brand-new point starts only on a line
        // whose first token is a frequency AND the previous point is complete
        // (or no point is in progress).
        int lineNo = 0;
        bool inNoise = false;
        double? pendingFreq = null;
        int pendingLineNo = 0;
        var pendingValues = new List<double>();

        int FlushPending()
        {
            if (pendingFreq is null) return 0;
            CommitPoint(doc, pendingFreq.Value, pendingValues, pendingLineNo);
            pendingFreq = null;
            pendingValues.Clear();
            return 1;
        }

        for (int idx = firstIdx; idx < allLines.Length; idx++)
        {
            lineNo++;
            var raw = allLines[idx];
            var trimmed = raw.Trim();
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

            var tokens = SplitTokens(trimmed);
            if (tokens.Length == 0) continue;

            // Does this line start a brand-new frequency point? Only when the
            // previous point is finished. Continuation lines (pure values,
            // possibly indented) always continue the in-progress point.
            bool startsNewPoint = pendingFreq is null || pendingValues.Count >= NeededValueCount(doc.PortCount);
            if (startsNewPoint && TryParseDouble(tokens[0], out var freq))
            {
                FlushPending();
                pendingFreq = freq;
                pendingLineNo = lineNo;
                for (int i = 1; i < tokens.Length; i++)
                    AppendValue(tokens[i], pendingValues, doc, lineNo);
            }
            else
            {
                for (int i = 0; i < tokens.Length; i++)
                    AppendValue(tokens[i], pendingValues, doc, lineNo);
            }

            // Commit once the point has all the pairs it needs.
            if (pendingFreq is not null && pendingValues.Count >= NeededValueCount(doc.PortCount))
                FlushPending();
        }

        // Trailing partial point (data ends mid-row).
        FlushPending();

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

    private static bool LooksLikeTouchstone(string first)
    {
        var t = first.Trim();
        // v1: comment-style header "freq mag angle" (rare). v2: "# GHz S RI R 50"
        if (t.StartsWith("#") || t.StartsWith("!"))
            return true;

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

        var tokens = body.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        // Touchstone v2.0/v2.1 option line: keywords may be in any order, but
        // each keyword has a fixed meaning. Scan the tokens and pick out
        // freq_unit, param type, data format, and reference resistance.
        for (int idx = 0; idx < tokens.Length; idx++)
        {
            string tok = tokens[idx];

            if (MatchesFreqUnit(tok))
            {
                doc.FrequencyUnit = ParseFreqUnit(tok);
            }
            else if (IsParamType(tok))
            {
                doc.ParamType = ParseParamType(tok);
            }
            else if (IsDataFormat(tok))
            {
                doc.DataFormat = ParseDataFormat(tok);
            }
            else if (tok.Equals("R", StringComparison.OrdinalIgnoreCase) && idx + 1 < tokens.Length)
            {
                if (double.TryParse(tokens[idx + 1], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var r))
                    doc.ReferenceResistance = r;
            }
            else
            {
                doc.HeaderComments.Add(tok);
            }
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

    private static int NeededValueCount(int portCount) => portCount * portCount * 2;

    private static string[] SplitTokens(string line) =>
        line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static void AppendValue(string token, List<double> values, TouchstoneDocument doc, int lineNo)
    {
        if (TryParseDouble(token, out var parsed))
            values.Add(parsed);
        else
            AddWarning(doc, $"Line {lineNo}: non-numeric token '{token}' ignored.");
    }

    private static void AddWarning(TouchstoneDocument doc, string message)
    {
        // Cap warnings: a pathological file could otherwise accumulate
        // thousands of duplicate messages and the UI binds them all.
        const int maxWarnings = 100;
        if (doc.Warnings.Count < maxWarnings)
            doc.Warnings.Add(message);
        else if (doc.Warnings.Count == maxWarnings)
            doc.Warnings.Add("... further warnings suppressed ...");
    }

    private static void CommitPoint(TouchstoneDocument doc, double freq, List<double> values, int lineNo)
    {
        int n = doc.PortCount;
        int needed = NeededValueCount(n);

        if (values.Count < needed)
        {
            AddWarning(doc,
                $"Line {lineNo}: expected {needed} values for {n}-port data, found {values.Count}. Parsed as partial.");
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
            AddWarning(doc, $"Noise line {lineNo}: bad frequency.");
            return;
        }

        if (!double.TryParse(tokens[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var nfDb))
        {
            AddWarning(doc, $"Noise line {lineNo}: bad noise figure.");
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
