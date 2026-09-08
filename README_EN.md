// SPDX-License-Identifier: GPL-3.0-or-later

# QuickLook.Plugin.SnpViewer

[中文](README.md)

A [QuickLook](https://github.com/QL-Win/QuickLook) plugin (Windows) that renders
Touchstone S-parameter files (`.s1p`, `.s2p`, …, `.snp`, `.ts`) as OxyPlot
charts when you press **Space** in File Explorer.

![screenshot](screenshot.png)

## Features

- Parses **Touchstone v1.1 / v2.x**: RI / MA / DB data formats, S / Y / Z / G / H
  parameter types, Hz / kHz / MHz / GHz frequency units, reference resistance.
- Handles **wrapped data rows**: multi-port files often split one frequency
  point across several physical lines; the parser re-assembles the value
  stream (fixed a bug where continuation lines were misread as negative
  frequencies).
- Handles `[Noise]` blocks (noise figure + equivalent noise resistance).
- Plots every `Sij` (or `Yij` / `Zij` / …) trace on one chart:
  - Y-axis pills: dB / Magnitude / Real / Imaginary / Phase (°);
  - X-axis: Linear / Log;
  - Legend arranged as a port-count matrix (s2p → 2×2, s3p → 3×3, …),
    click entries to show/hide traces;
  - Hover tooltips with data-point readouts.
- Follows the Windows light / dark app mode (reads `AppsUseLightTheme`; chart,
  option bar, legend and tooltips all re-skin).
- UI language follows the system display language: Chinese on zh-* systems,
  English otherwise.
- Large-file guardrails: plotted traces are decimated to ≤ 4000 points each
  so OxyPlot never freezes.
- Header sniffing lets non-Touchstone files through so the plugin never steals
  previews from other viewers.
- More-menu entries: open in the default text editor, reveal in Explorer.

## Supported extensions

```
.s1p .s2p .s3p .s4p .s5p .s6p .s7p .s8p .snp .ts
```

## Installing (users)

Download `QuickLook.Plugin.SnpViewer.qlplugin` from
[Releases](https://github.com/riverstill/quicklook-plugin-snpviewer/releases),
select it in File Explorer, press **Space**, and click Install.

Manual install also works:

1. Quit QuickLook (tray icon → Quit).
2. Unzip the `.qlplugin` (it is a ZIP) and copy the contents into a
   `QuickLook.Plugin.SnpViewer` folder under your plugin directory (create it
   if needed). For the Installer build this is usually
   `%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\`; other distributions are
   listed in the
   [official wiki](https://github.com/QL-Win/QuickLook/wiki/Differences-Between-Distributions#user-data-location).
3. Start QuickLook, select a `.s2p` file and press **Space**.

## Building / testing / packaging (Windows only)

> Linux cannot build this (WPF / net462). The `Build` workflow on CI
> (`windows-latest` + .NET SDK 8) is the source of truth — do not bother with
> a local `dotnet build` there.

```cmd
:: Compile check
dotnet build QuickLook.Plugin.SnpViewer.sln -c Release

:: Tests
dotnet test tests\QuickLook.Plugin.SnpViewer.Tests.csproj

:: Single test
dotnet test tests\QuickLook.Plugin.SnpViewer.Tests.csproj --filter "FullyQualifiedName~<TestName>"

:: Package (QuickLook.Plugin.SnpViewer.qlplugin lands in the repo root)
powershell -ExecutionPolicy Bypass -File package.ps1 -Configuration Release -Platform AnyCPU
```

Notes:

- The `.sln` names the platform **`Any CPU` (with space)** while the `.csproj`
  says **`AnyCPU` (no space)** — keep both spellings in sync or the build
  fails with MSB4126.
- `.qlplugin` files must be produced by `package.ps1` (it goes through
  `dotnet publish` so NuGet dependency DLLs are staged reliably). Never
  hand-roll the zip, and never zip the raw `Build\...` directory.
  Package contract: plugin DLL + `OxyPlot*.dll` + `Translations.config` +
  `QuickLook.Plugin.Metadata.config`, all at the zip root; **excludes**
  `QuickLook.Common.dll` (provided by the host), `*.pdb` and caches.
- Bump `<Version>` in `QuickLook.Plugin.Metadata.config` on every release —
  the spacebar installer reads that file.

The tests parse the fixtures in `samples/` (`.s1p` / `.s2p`, including a
`[Noise]` block) and assert exact numeric values; add a regression test when
you touch the parser.

## Project layout

```
.
├── QuickLook.Plugin.SnpViewer/
│   ├── Plugin.cs                         # IViewer (sniffing + preview entry)
│   ├── Plugin.MoreMenu.cs                # IMoreMenu (open / reveal)
│   ├── SnpPanel.xaml(.cs)                # WPF panel + OxyPlot viewmodel
│   ├── ThemeHelper.cs                    # System theme detection + plot palette + language pick
│   ├── TouchstoneParser.cs               # Touchstone v1/v2 text parser
│   ├── TouchstoneDocument.cs             # Data model
│   ├── Translations.config
│   ├── QuickLook.Plugin.Metadata.config  # Spacebar-installer metadata (incl. version)
│   └── QuickLook.Plugin.SnpViewer.csproj
├── samples/                              # Parser fixtures
├── tests/                                # xUnit tests
├── package.ps1                           # Builds the .qlplugin (CI and local share it)
├── build.cmd / build.sh                  # Convenience build wrappers
├── AGENTS.md                             # Repo-specific notes for AI assistants
└── LICENSE-GPL.txt
```

Dependencies (pulled automatically from NuGet, nothing to install by hand):
`OxyPlot.Wpf 2.1.2` (NOT 2.2+ — the legend API differs), `QuickLook.Common
4.5.0`. Target framework: net462.

## Troubleshooting

- "Failed to preview" with a green build: read this plugin's stack in
  `%AppData%\pooi.moe\QuickLook\QuickLook.Exception.log` first — do not guess.
- The known traps are recorded in `AGENTS.md` (BAML load context, frozen
  brushes must not be mutated, `Plot.Model` must be assigned in code, …);
  read it before touching UI code.

## License

GPL-3.0-or-later, see `LICENSE-GPL.txt`. All source files carry an SPDX header.

## Acknowledgements

- [QuickLook](https://github.com/QL-Win/QuickLook) by the QL-Win contributors.
- [OxyPlot](https://github.com/oxyplot/oxyplot) for the WPF charting engine.
- [Touchstone File Format Specification v2.1](https://ibis.org/touchstone_ver2.1/)
  (IBIS Open Forum).
