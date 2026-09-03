// SPDX-License-Identifier: GPL-3.0-or-later

using QuickLook.Common.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace QuickLook.Plugin.SnpViewer;

public sealed partial class Plugin : IViewer
{
    private static readonly string[] Extensions =
    {
        ".s1p", ".s2p", ".s3p", ".s4p", ".s5p", ".s6p", ".s7p", ".s8p", ".snp", ".ts"
    };

    private SnpPanel? _panel;
    private string? _currentPath;

    public int Priority => 0;

    public void Init()
    {
    }

    public bool CanHandle(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (Directory.Exists(path)) return false;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return false;
        if (!Extensions.Any(e => ext.Equals(e, StringComparison.OrdinalIgnoreCase))) return false;

        // Lightweight sniffing: file must start with '#', '!', or a number.
        try
        {
            using var fs = File.OpenRead(path);
            using var sr = new StreamReader(fs);
            string? first = null;
            for (int i = 0; i < 10; i++)
            {
                var line = sr.ReadLine();
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;
                first = line.Trim();
                break;
            }
            if (string.IsNullOrEmpty(first)) return false;
            if (first.StartsWith("#") || first.StartsWith("!")) return true;
            var firstTok = first.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrEmpty(firstTok)) return false;
            return double.TryParse(firstTok, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _);
        }
        catch
        {
            return false;
        }
    }

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size(1100, 720);
    }

    public void View(string path, ContextObject context)
    {
        _panel = new SnpPanel();
        _currentPath = path;
        context.ViewerContent = _panel;
        context.Title = Path.IsPathRooted(path) ? Path.GetFileName(path) : path;

        _panel.LoadFile(path);

        _panel.Dispatcher.Invoke(() => { context.IsBusy = false; }, DispatcherPriority.Loaded);
    }

    public void Cleanup()
    {
        GC.SuppressFinalize(this);

        _panel = null;
        _currentPath = null;
    }
}
