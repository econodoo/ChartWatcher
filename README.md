# Chart Watcher 📊

A WinUI 3 desktop wallboard that mirrors live web-based market dashboards (SSI iBoard, Fialda, etc.) into a configurable, themable workspace.

## Quick Start

### Prerequisites
- **Windows 10 (19041+)** or Windows 11
- **Visual Studio 2022 17.9+** with:
  - .NET 8 SDK
  - Windows App SDK 1.6 workload
  - WebView2 Runtime (usually pre-installed on Windows 10/11)

### Build & Run
```bash
# Clone/extract the project
cd ChartWatcher

# Restore & build
dotnet restore ChartWatcher.sln
dotnet build src/ChartWatcher.UI/ChartWatcher.UI.csproj -c Debug -p:Platform=x64

# Run
dotnet run --project src/ChartWatcher.UI/ChartWatcher.UI.csproj -c Debug -p:Platform=x64
```

Or open `ChartWatcher.sln` in Visual Studio, set **ChartWatcher.UI** as startup project, platform **x64**, and press F5.

## Architecture

```
ChartWatcher.sln
├── ChartWatcher.Core          # Domain models, no dependencies
│   ├── Sources/               # Source entity + ISourceRepository
│   ├── Components/            # Component, RenderMode, ComponentType
│   ├── Stickers/              # Selector cascade, value formats
│   ├── Workspaces/            # Workspace, Tab, Placement, GridSpec
│   └── Thresholds/            # Threshold rules for alerts
├── ChartWatcher.Application   # ViewModels + services
│   ├── Services/              # ThemeService, ISourceHub
│   └── ViewModels/            # Shell, Tab, Component VMs
├── ChartWatcher.Infrastructure # Persistence + WebView bridge
│   ├── Persistence/           # SQLite repositories, DB initializer
│   └── WebView/               # SourceHub (source lifecycle)
├── ChartWatcher.UI            # WinUI 3 entry point
│   ├── Themes/                # Colorful, Stealth, ColorBlind
│   ├── Windows/               # ShellWindow (main dashboard)
│   └── Controls/              # ComponentCardControl (themable card)
└── ChartWatcher.Agent         # JS agent for WebView2 injection
    └── chartwatch-agent.js    # Selector engine, mutation watcher, picker
```

## Key Design Decisions

| Decision | Choice | Why |
|----------|--------|-----|
| **UI Framework** | WinUI 3 (Windows App SDK 1.6) | Strict WinUI 3 only — no WPF, no WinUI 2 |
| **Packaging** | Unpackaged (`WindowsPackageType=None`) | Personal use, simpler deployment |
| **Persistence** | SQLite via `Microsoft.Data.Sqlite` | Zero-config, file-based, portable |
| **MVVM** | CommunityToolkit.Mvvm 8.3.2 | Source generators, minimal boilerplate |
| **Web Content** | WebView2 | Render live market sites, inject JS agent |
| **Theming** | 3 ResourceDictionary files, runtime swap | "Boring to watch market with same app daily" |

## Themes

- **Colorful** — Vivid green/red on deep blue. Classic trading terminal feel.
- **Stealth** — Muted amber/teal on charcoal. Low-distraction night mode.
- **ColorBlind** — IBM accessible palette: blue=up, orange=down. Safe for protanopia/deuteranopia.

Switch via the toolbar theme dropdown or `Ctrl+T` (planned).

## Grid System

12 columns × 8 rows. Components placed with integer Row/Col/RowSpan/ColSpan.
Drag-and-snap in design mode. View mode hides the grid and title bars.

## Database

Auto-created at `%LOCALAPPDATA%\ChartWatcher\chartwatcher.db`.
Tables: `sources`, `components`, `workspaces`, `tabs`, `placements`, `settings`.

## Phase Roadmap

| Phase | Status | What |
|-------|--------|------|
| 1. Shell | ✅ Done | Window, toolbar, panels, tabs, status bar |
| 2. Picker | 🔜 Next | Open source URL → inject JS agent → element picker overlay |
| 3. Crop | Planned | CSS-based viewport isolation |
| 4. Grid | ✅ Done | 12×8 grid, card placement, inspector |
| 5. Themes | ✅ Done | 3 themes, runtime switching |
| 6. AI text | Deferred | Companion narration |
| 7. Voice | Deferred | TTS output |
| 8. Informed | Deferred | Contextual AI commentary |
