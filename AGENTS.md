# AGENTS.md — quicklook-plugin-snpviewer

QuickLook (Windows, Space-to-preview) plugin that renders Touchstone
S-parameter files (`.s1p`–`.snp`, `.ts`) as OxyPlot charts. C# / WPF /
.NET Framework 4.6.2. License: GPL-3.0-or-later (all files carry the SPDX header).

## Cannot build on Linux — use CI as source of truth

- No .NET SDK here and WPF/net462 cannot compile on Linux anyway. Never try
  `dotnet build` locally; push and read the `Build` workflow result instead.
- CI runs on `windows-latest` with .NET SDK 8 (`actions/setup-dotnet@v4`,
  `dotnet-version: 8.0.x`). That SDK version is load-bearing for net462+WPF.

## Build / test / package (all Windows-only)

- Compile check: `dotnet build QuickLook.Plugin.SnpViewer.sln -c Release`
- Tests: `dotnet test tests/QuickLook.Plugin.SnpViewer.Tests.csproj`
- Single test: `dotnet test tests/QuickLook.Plugin.SnpViewer.Tests.csproj --filter "FullyQualifiedName~<TestName>"`
- Ship artifact: `./package.ps1 -Configuration Release -Platform AnyCPU`
  (requires .NET SDK; produces `QuickLook.Plugin.SnpViewer.qlplugin` in repo root).
  CI calls this same script — never hand-roll the zip.

## Solution Platform quirk (has broken CI before)

- The `.sln` names the platform **`Any CPU` (with space)**; the `.csproj`
  conditions match **`AnyCPU` (no space)**. The workflow maps one to the other
  (`-p:Platform="Any CPU"` for the solution, `-p:Platform=AnyCPU` for
  `dotnet publish` of the single project). If you add configurations, keep
  both spellings in sync or the build fails with MSB4126.

## Packaging rules — the .qlplugin is a contract, not just a zip

`package.ps1` enforces these with fail-fast throws; keep the assertions if you
touch it:

- Build the zip from **`dotnet publish` output, never from `Build\...`**.
  Raw build output does not reliably contain NuGet dependency DLLs.
- Ship: plugin DLL + `OxyPlot*.dll` + `Translations.config` +
  `QuickLook.Plugin.Metadata.config` (all at zip root, no subfolders).
- Exclude: `QuickLook.Common.dll` (host provides it), `*.pdb`, caches.
- `QuickLook.Plugin.Metadata.config` is REQUIRED for spacebar-install:
  QuickLook's built-in PluginInstaller reads `/Metadata/Namespace` (must start
  with `QuickLook.Plugin.`) and `/Metadata/Version` from the zip entry named
  exactly that. Bump `<Version>` on every release.
- Install target (Installer build): `%AppData%\pooi.moe\QuickLook\QuickLook.Plugin\`.

## Runtime gotchas (each one caused a real user-visible bug)

- **BAML load context**: the host loads the plugin via `Assembly.LoadFrom`,
  but WPF's BAML parser resolves `oxy:` namespaces in the default context
  (GAC + host exe dir), so `OxyPlot.Wpf.dll` next to our DLL is invisible.
  `Plugin.Init()` registers an `AppDomain.AssemblyResolve` handler probing our
  own directory (skipping `*.resources` and `QuickLook.Common`). Do not remove
  it; any new third-party dep rides on it automatically.
- **No XAML binding for `Plot.Model`**: assign in code-behind
  (`Plot.Model = ...; Plot.InvalidatePlot()`). Going via DataContext/Binding
  is racy on first show and needs manual invalidation on rebuilds.
- `SnpPanel.LoadFile` is `async void` into the host: keep the whole
  parse+render inside try/catch and surface errors via `Summary`, never let
  exceptions escape (host shows "Failed to preview" otherwise).
- **NEVER mutate a brush that is already in use**: WPF freezes `Freezable`
  objects once the rendering layer has consumed them; `brush.Color = ...`
  later throws `InvalidOperationException` ("object is in a read-only state")
  and kills the whole preview from the ctor. To theme-switch, REPLACE the
  entry in the ResourceDictionary (`Resources[key] = new SolidColorBrush(...)`)
  and keep every consumer on `{DynamicResource}` (they re-resolve on swap).
  This caused a multi-commit "Failed to preview" hunt; check
  `%AppData%\pooi.moe\QuickLook\QuickLook.Exception.log` first whenever the
  build is green but preview dies.
- Host crash reports land in `%AppData%\pooi.moe\QuickLook\QuickLook.Exception.log`
  — ask for it before guessing.

## Code map

- `QuickLook.Plugin.SnpViewer/Plugin.cs` — `IViewer` (`CanHandle` sniffing,
  `View`), `Plugin.MoreMenu.cs` — `IMoreMenu` (uses
  `QuickLook.Common.Commands.RelayCommand`, not a local copy).
- `TouchstoneParser.cs` — v1.1/v2.x text parser; option-line keywords are
  classified **one token at a time** (`# GHz S MA R 50` order is fixed but each
  token stands alone — a two-token-lookahead classifier silently drops them).
- `TouchstoneDocument.cs` — model. `SnpPanel.xaml(.cs)` — WPF view + viewmodel.
- `samples/` — parser fixtures (`termination.s1p`, `filter_2port.s2p`,
  `amplifier_2port.s2p` incl. `[Noise]`); tests assert exact numeric values.
- Deps: `OxyPlot.Wpf 2.1.2` (NOT 2.2+: `IsLegendEnabled`/`LegendPosition` don't
  exist there), `QuickLook.Common 4.5.0` via NuGet (never a submodule).
- `net462` has no `string.Contains(x, StringComparison)` — use `IndexOf`.
- `ThemeHelper.cs` — `ThemeHelper` (system light/dark via `AppsUseLightTheme`,
  `PlotTheme` palettes incl. legend colors), `Lang` (zh-* → Chinese else
  English, read once via `CurrentUICulture`, all UI strings via `Lang.Pick`).

## Workflow (owner rules — do not skip)

- **Review before push**: never commit or push without the owner's explicit
  confirmation. Show the diff first.
- The words "推送 / 提交 / 发版" (push / commit / release) **are** the approval —
  execute immediately, no further questions.
- Release procedure (all steps, in order):
  1. Bump `<Version>` in `QuickLook.Plugin.Metadata.config`, commit only that file.
  2. `git tag vX.Y.Z`, push main + tag.
  3. Wait for the **tag** CI run (workflow also runs on `v*` tags).
  4. Download the tag-run artifact, verify the 6 zip entries + metadata version.
  5. `gh release create vX.Y.Z <qlplugin> --title ... --notes ...` with
     bilingual (zh/en) notes. Docs-only changes never need a re-release.

## OxyPlot 2.1.2 pinned facts (each verified against source, not memory)

- `PlotModel.Legends` is **empty by default** — add an explicit `Legend`,
  otherwise series titles never render.
- `Legend` has **no corner-radius property** (`DrawRectangle` only) — a boxed
  legend is always square; don't promise rounded.
- **Legend matrix**: `Vertical` items wrap to a new column when they exceed the
  available height, and Inside legends get `min(plotArea, LegendMaxHeight)`.
  So `LegendMaxHeight = 32 + N*18` (2×Margin + 2×Padding + N×~15px rows at
  11pt) yields an N×N grid (s2p → 2×2). Recalibrate if the font size changes;
  the wrap condition is `y + h > available - padding` in
  `Legends/Legend.Rendering.cs`.
- `Axis.AxisTitleDistance` defaults to **4**; Y title needed 14 to clear labels.
- Tracker text uses `{0}` title / `{1}` X-title / `{2}` X / `{3}` Y-title /
  `{4}` Y. There is **no** `PlotModel.DefaultTrackerFormatString` in 2.1.2 —
  set `TrackerFormatString` per series (named `{XAxis.Title}` also resolves via
  `StringHelper` reflection, but index tokens are clearer).
- Right side has no axis/title, so `Padding.Right = 0` + `PlotMargins.Right = 0`
  pins the plot area to the container edge; other sides stay NaN (auto).
- The `oxy:` xmlns maps the Shared assembly too, so `oxy:TrackerControl` in
  BAML resolves like `oxy:PlotView` — the earlier "tracker template crashes
  BAML" theory was wrong; the real killer was the frozen brush (see gotchas).

## Parser / rendering caps (robustness contract)

- `TouchstoneParser` accumulates wrapped rows: a new point starts only when the
  previous one is complete (`n²×2` values) — value-only continuation lines must
  never be parsed as frequencies (that produced negative-frequency garbage).
- Cap parser warnings at 100 (`AddWarning`), warning bar shows 4
  (`SyncWarnings`), plot decimates to ≤ 4000 pts/series (`plotEvery`).
  Pathological files must degrade, never hang or bloat the UI.
- Parser changes require a regression test with real pasted data
  (see the wrapped-s4p test) — temp files need the real extension (`.s4p`)
  or port-count sniffing defaults to 2.
