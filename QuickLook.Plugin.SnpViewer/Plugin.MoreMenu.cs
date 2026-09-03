// SPDX-License-Identifier: GPL-3.0-or-later

using QuickLook.Common.Commands;
using QuickLook.Common.Plugin;
using QuickLook.Common.Plugin.MoreMenu;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace QuickLook.Plugin.SnpViewer;

public sealed partial class Plugin : IMoreMenu
{
    public IEnumerable<IMenuItem> MenuItems => GetMenuItems();

    private IEnumerable<IMenuItem> GetMenuItems()
    {
        return new IMenuItem[]
        {
            new MoreMenuItem
            {
                Header = "Open in default text editor",
                ToolTip = "Edit the raw Touchstone file",
                Command = new RelayCommand(() =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _currentPath ?? string.Empty,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open: {ex.Message}");
                    }
                })
            },
            new MoreMenuItem
            {
                Header = "Open file location",
                ToolTip = "Reveal in File Explorer",
                Command = new RelayCommand(() =>
                {
                    if (string.IsNullOrEmpty(_currentPath)) return;
                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{_currentPath}\"");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open: {ex.Message}");
                    }
                })
            }
        };
    }
}
