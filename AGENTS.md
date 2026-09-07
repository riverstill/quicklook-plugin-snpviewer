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
- **No XAML binding for `Plot.Model` / grid source**: assign in code-behind
  (`Plot.Model = ...; Plot.InvalidatePlot()`,
  `DataGrid.ItemsSource = DataTable.DefaultView`). Binding a `DataTable`
  object itself silently shows nothing — it must be `.DefaultView`.
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
