# Desktop Stickman Universe

Autonomous Windows desktop stickman overlay application built with **.NET 8**, **WPF**, and **Win32** desktop integration. Stickman characters walk, jump, climb, interact with open windows and the taskbar, react to the mouse cursor, and stage emergent multi-character mini-events across your monitors.

## Control Center

Double-click the Stickmans tray icon or choose **Open Stickmans** from its menu.

- Start or stop one of six mini-events and see the current event and its remaining time. Turn off automatic mini-events to run the stage manually.
- Give each character a nickname and use **Find on desktop** to have the selected character call out. Nicknames are saved locally and stay with the crew when you change its size.
- Start a 15-, 25-, or 50-minute focus session to hide the crew and suspend the simulation. The app restores your previous visibility and pause settings when the session ends.

## Features

- **Desktop & Multi-Monitor Awareness**: Tracks open windows, DWM extended frame bounds, cloaked windows, taskbars, and per-monitor DPI scales using `SetWinEventHook` and Win32 APIs (`WindowTracker`, `DesktopEnvironmentSnapshot`).
- **2D Physics & Surface Carry**: Deterministic physics engine with gravity, swept landings, window-movement carry, wall climbing/mantling, monitor boundary handling, and character separation (`PhysicsEngine`).
- **Autonomous AI & Mini-Events**: Characters react to the cursor and organize duels, tag chases, chats, dance parties, acrobatics shows, and window climbs. To start one yourself, right-click the system tray icon and choose **Mini-Events**.
- **Procedural Animation**: Skeleton rig and procedural animation (`SkeletonRig`, `ProceduralAnimator`) rendered smoothly onto transparent per-monitor overlays (`OverlayManager`, `CharacterRenderer`).
- **Mouse & Tray Controls**: Grab/throw characters, hover and cursor reactions, right-click character menu, and system tray controls (`Pause`/`Resume`, `Scatter Characters`, `Settings...`, `Exit`).
- **Settings & Live Telemetry**: Configure character count (1–10), custom character colors, animation speed (0.5x–2.0x), activity level (`Calm`, `Normal`, `Lively`), playful fighting, mouse interactions, Performance Mode (Eco), Windows startup launch (`HKCU` Run key), and live FPS/CPU/memory/scan telemetry (`SettingsWindow`).

## Requirements

- Windows 10 / 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (`net8.0-windows`)

## Getting Started

```powershell
# Restore & build
dotnet build DesktopStickmanUniverse.sln

# Run the application
dotnet run --project src/DesktopStickmanUniverse/DesktopStickmanUniverse.csproj

# Run unit tests
dotnet test DesktopStickmanUniverse.sln
```

## Project Structure

```text
DesktopStickmanUniverse.sln
├── src/DesktopStickmanUniverse/
│   ├── AI/            # Autonomous behavior & multi-character mini-event director
│   ├── Animation/     # Procedural animator & skeleton rig
│   ├── Core/          # StickmanUniverseEngine main simulation coordinator
│   ├── Desktop/       # WinEventHook window & monitor geometry tracker
│   ├── Interaction/   # Mouse grab, hover, and context menu interactions
│   ├── Interop/       # Win32 P/Invoke definitions
│   ├── Overlay/       # Transparent per-monitor WPF overlay windows
│   ├── Performance/   # FPS, CPU, memory, and scan telemetry monitor
│   ├── Physics/       # 2D physics engine & stickman body state
│   ├── Rendering/     # Stickman & particle rendering
│   ├── Settings/      # Persistent JSON settings & Windows startup helper
│   └── UI/            # Settings window & system tray manager
└── tests/DesktopStickmanUniverse.Tests/
    └── ...            # xUnit test suite
```
