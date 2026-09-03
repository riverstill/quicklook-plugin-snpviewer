// SPDX-License-Identifier: GPL-3.0-or-later

using QuickLook.Common.Plugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace QuickLook.Plugin.SnpViewer;

public sealed partial class Plugin : IViewer
{
    private static readonly string[] Extensions =
    {
        ".s1p", ".s2p", ".s3p", ".s4p", ".s5p", ".s6p", ".s7p", ".s8p", ".snp", ".ts"
    };

    private static int _assemblyResolverRegistered;

    private SnpPanel? _panel;
    private string? _currentPath;

    public int Priority => 0;

    public void Init()
    {
        EnsureAssemblyResolverRegistered();
    }

    private static void EnsureAssemblyResolverRegistered()
    {
        // Idempotent: Plugin instances are created on demand by QuickLook and
        // may be disposed; only wire the resolver once per AppDomain.
        if (System.Threading.Interlocked.Exchange(ref _assemblyResolverRegistered, 1) != 0)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
    }

    /// <summary>
    /// BAML assembly resolution (triggered by WPF when parsing XAML that uses
    /// a custom namespace prefix such as <c>oxy:PlotView</c>) runs in the
    /// default load context, which only probes the GAC and the host executable
    /// directory. The QuickLook host loads this plugin via
    /// <see cref="Assembly.LoadFrom(string)"/>, so dependencies that live
    /// alongside our DLL (e.g. OxyPlot.Wpf.dll) are invisible to BAML. We
    /// resolve them by hand from the directory that contains our own assembly.
    /// </summary>
    private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrEmpty(name))
            return null;

        // Skip resource assemblies: PublicKeyToken is set, Name has ".resources"
        if (name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
            return null;

        // The host provides QuickLook.Common.dll in its own directory; do not
        // shadow that resolution with a stale copy from our plugin folder.
        if (name.Equals("QuickLook.Common", StringComparison.OrdinalIgnoreCase))
            return null;

        string? pluginDir = null;
        try
        {
            var self = typeof(Plugin).Assembly;
            var location = self.Location;
            if (!string.IsNullOrEmpty(location))
                pluginDir = Path.GetDirectoryName(location);
        }
        catch
        {
            // ignored
        }

        if (string.IsNullOrEmpty(pluginDir))
            return null;

        var candidate = Path.Combine(pluginDir, name + ".dll");
        if (!File.Exists(candidate))
            return null;

        try
        {
            return Assembly.LoadFrom(candidate);
        }
        catch
        {
            return null;
        }
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
