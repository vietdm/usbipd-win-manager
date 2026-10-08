# USBIPD Manager: Implementation Plan

- Created: 2026-10-08
- Updated: 2026-10-08
- Status: CONFIRMED by the maintainer (answers in section 12)
- Author: AI-generated, reviewed by the maintainer

## 1. Background

The maintainer develops Flutter apps inside WSL2 and debugs them on physical Android devices over USB.
WSL2 has no direct USB access. [usbipd-win](https://github.com/dorssel/usbipd-win) solves that by sharing USB devices over USB/IP,
but a device can only belong to **one side at a time**: Windows or WSL2. Both sides are needed regularly, so switching by hand on the command line is tedious.

Relevant usbipd-win behavior:

```powershell
# Share a device (one time, admin). The device stays shared ("bound") across detach/attach cycles.
usbipd bind --busid <BUSID>
usbipd unbind --busid <BUSID>

# Give the device back to Windows. The device stays bound; next time only attach is needed.
usbipd detach --busid <BUSID>
usbipd detach --all

# Attach the device to WSL2 (a WSL2 distro must be running)
usbipd attach --wsl --busid <BUSID>

# Machine-readable device state (JSON)
usbipd state
```

Unplugging the cable also detaches the device.

## 2. Goal

A Windows desktop app that runs in the background (tray app, like Unikey) and switches USB devices between Windows and WSL2 with one click.

## 3. Requirements (from the maintainer, translated)

1. The app runs with Windows administrator rights. The user grants the permission.
2. The app starts with Windows and hides in the system tray. Registering autostart may ask for elevation once, but at logon it must start **elevated without a UAC prompt**.
3. Double-clicking the tray icon opens a compact app window with:
   1. A console panel that logs every action, including startup status (usbipd-win installed, usbipd service ready, WSL available, ...).
   2. Three buttons: **Init** (re-check and auto-fix: install usbipd-win, start the service), **Windows** (move managed devices to Windows), **WSL2** (move managed devices to WSL2).
   3. A footer: gear icon on the left (Settings), copyright in the middle, Help on the right.
      Help shows "Send issues to email vietdau33@gmail.com".
4. The UI is designed with the `ui-ux-pro-max` skill.
5. Minimize and maximize are disabled. X hides the window to the tray. Right-clicking the tray icon shows a menu (section 6.6).
   Exit fully quits the app and stops its background work. About shows the author, dates and version.
6. Distribution: a portable exe by default, an installer when requested (section 8).
7. A build script increments the version on every build. The version can also be set manually.
8. Version format `MAJOR.MINOR.PATCH`. An automatic build bumps PATCH. Manual input `2`, `2.2` or `2.2.5` must be greater than the current version.
9. Complete AI Memory. `CLAUDE.md` (and `AGENTS.md` for Codex) route the agent correctly for every task.
10. Everything in code, in the app UI and in AI Memory is written in English.

## 4. Technology

| Concern | Choice | Reason |
| --- | --- | --- |
| Language / UI | **C# + WPF on .NET 10 (LTS)** | Native Windows, full custom styling, first-class elevation/tray/Task Scheduler support. `ui-ux-pro-max` has a `wpf` stack guide. |
| Tray icon | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`) | No third-party dependency. |
| Device change events | WMI `Win32_DeviceChangeEvent` (`System.Management`) + periodic refresh | No window handle needed in the core library. |
| Packaging | `dotnet publish` self-contained, single-file, win-x64 | Runs on a machine with no .NET runtime installed. |
| Installer | **Inno Setup 6** | Free, scriptable, admin install, upgrade in place, uninstall hooks. |
| Tests | xUnit | Parsers, device rules, version logic and mode logic are testable without USB hardware. |

Dev toolchain (installed by the maintainer on 2026-10-08): .NET SDK 10.0.401, Inno Setup 6 (`%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe`), Python 3.13 (only for the `ui-ux-pro-max` search scripts at design time), git.
usbipd-win itself is not needed to build; the app's Init button installs it.

## 5. Architecture

```
usbipd-win-manager/
├── CLAUDE.md / AGENTS.md          # Agent routing
├── README.md
├── version.json                   # { version, createdDate, updatedDate }
├── build.ps1 / build.bat          # Build + version bump + publish (+ installer)
├── installer/UsbipdManager.iss    # Inno Setup script
├── tools/                         # Dev helpers (icon generation, ...)
├── src/
│   ├── UsbipdManager.Core/        # Class library, no WPF. All logic, testable.
│   │   ├── Models/                # UsbDevice, ManagedDevice, UsbMode, AppSettings, LogEntry, EnvironmentReport, ...
│   │   ├── Abstractions/          # Interfaces (the contract between modules)
│   │   ├── Processes/             # ProcessRunner
│   │   ├── Usbipd/                # UsbipdClient, UsbipdStateParser, DeviceKey, UsbipdServiceController, UsbipdInstaller
│   │   ├── Wsl/                   # WslClient (+ keep-alive process)
│   │   ├── Logging/               # LogService (memory feed + rolling file)
│   │   ├── Settings/              # SettingsStore (JSON)
│   │   ├── Services/              # EnvironmentChecker, InitService, DeviceManager, ModeSwitcher, AutoAttachWatcher
│   │   └── Platform/              # AutoStartManager, ShortcutManager, SingleInstance, DeviceChangeNotifier, AppInfo
│   └── UsbipdManager/             # WPF exe: composition root, windows, theme, tray
│       ├── app.manifest           # requireAdministrator, PerMonitorV2
│       ├── App.xaml(.cs)
│       ├── Assets/
│       ├── Theme/
│       ├── ViewModels/
│       └── Views/                 # MainWindow, SettingsWindow, AboutWindow, MessageDialog
├── tests/UsbipdManager.Tests/
└── docs/
    ├── PLAN.md
    └── ai-memory/
```

All `usbipd` / `wsl` / `schtasks` calls go through `IProcessRunner`, so every command and its output is logged and can be faked in tests.

## 6. Behavior specification

### 6.1 Elevation, autostart, single instance

- `app.manifest` uses `requireAdministrator`: a manual launch shows UAC once.
- "Start with Windows" ON creates a Task Scheduler task (`schtasks /Create /XML`):
  logon trigger for the current user, action `UsbipdManager.exe --tray`, `RunLevel = HighestAvailable` → starts elevated with **no UAC prompt**,
  no execution time limit, runs on battery.
  OFF deletes the task. The Settings switch reads the real task state.
- On every startup, if the task exists but points to a different exe path (portable exe moved), the task is re-registered with the current path.
- Single instance: a second launch signals the running instance to show its window, then exits.
- Launched with `--tray`: starts hidden in the tray. Launched manually: shows the window.

Command-line contract (shared by the app, the installer and the autostart task):

| Argument | Behavior | Exit code |
| --- | --- | --- |
| (none) | Start normally and show the window. | - |
| `--tray` | Start hidden in the tray (used by the logon task). | - |
| `--register-autostart` | Headless: create/refresh the logon task for this exe path, then exit. | 0 ok, 1 failed |
| `--unregister-autostart` | Headless: delete the logon task, then exit. | 0 ok, 1 failed |
| `--exit` | Headless: ask the running instance to release devices and quit, wait up to 20 s for it to end, then exit. | 0 ok (or nothing running), 1 timeout |

A second normal launch only activates the running instance's window.

### 6.2 Startup checks (read-only, logged)

1. Running as administrator.
2. usbipd-win installed + version.
3. `usbipd` Windows service: exists / running / startup type.
4. WSL installed. WSL2 distros, their state, the default distro.
5. Devices with state: Not shared / Shared / Attached, plus remembered devices that are disconnected.
6. Current mode.

**Unsupported machine**: if WSL is not installed (or no WSL2 distro exists) the console shows a red error
"WSL is not installed on this machine. USBIPD Manager is not supported." and the Windows/WSL2 buttons, the device toggles and the tray switch items are disabled.
Init stays enabled only to re-run the checks (e.g. after the user installs WSL). WSL is never installed automatically.

**usbipd missing**: Windows/WSL2 are disabled until Init installs it.

### 6.3 Init button

Re-runs 6.2, then fixes what it can:

- usbipd missing → `winget install --id dorssel.usbipd-win -e --silent --accept-package-agreements --accept-source-agreements`. winget missing → log the release page URL.
- Service stopped → start it. Startup type Disabled → set it to Automatic.
- Managed devices that are connected but not shared → `usbipd bind`.
- Buttons are disabled while any operation runs; the console shows progress.

### 6.4 Managed devices (per port + per device)

The main window shows a device list. Each connected device has an on/off switch ("managed").

- A managed entry remembers **BusId (port) + DeviceKey (device identity)** + description.
- DeviceKey: `VID:PID` is not stable for Android (the PID changes with the USB mode: MTP / PTP / ADB), so the key is `VID + serial` when the instance ID carries a real serial number, otherwise the full instance ID (which Windows derives from the port).
- Switch ON → `usbipd bind --busid`, remember the entry, and attach immediately if the mode is WSL2. Input-like devices (keyboard, mouse, Bluetooth, HID) ask for confirmation first.
- Switch OFF → detach if attached, `usbipd unbind --busid`, forget the entry.
- Automatic rules on every device change:
  - The remembered device appears again on its remembered port → treated as ON: bind if needed, attach if the mode is WSL2.
  - A different device appears on a remembered port → OFF (nothing happens to it).
  - A remembered device is unplugged → the entry stays, shown as "Disconnected", with a "Forget" action.
- Unmanaged devices are never touched by the app (no bind/unbind), so manual `usbipd` usage stays intact.

### 6.5 Mode switching

The **mode** is the user's choice (Windows or WSL2), persisted in settings.

- **Windows**: stop the auto-attach watcher, stop the WSL keep-alive, `usbipd detach --all`. Devices stay bound.
- **WSL2**: make sure a WSL2 distro is running (start a hidden keep-alive process `wsl.exe -d <distro> --exec sleep infinity` owned by the app), then `usbipd attach --wsl --busid` for every connected managed device. Start the auto-attach watcher: a re-plugged managed device is attached again automatically.
- The tray icon and tooltip show the mode.

### 6.6 Window chrome and tray

- Compact fixed-size window with a custom title bar: minimize and maximize are shown disabled; X hides the window to the tray.
- Tray double-click → show and activate the window.
- Tray right-click menu: **Open**, **Switch to Windows**, **Switch to WSL2**, separator, **About**, **Exit**.
  The item for the current mode is disabled. Both switch items are disabled while the machine is unsupported or an operation runs.
- **Exit**: return all devices to Windows (`usbipd detach --all`) without changing the saved mode, stop the watcher and the keep-alive process, dispose the tray icon, quit.
  The usbipd Windows service keeps running.
  On the next launch, if the saved mode is WSL2 and "Restore last mode on startup" is ON, the managed devices are attached to WSL2 again.
- **About**: app name, version, author **Minh Viet**, created date, updated date (if any), copyright, issue email.

### 6.7 Footer

- Left: gear icon button (accessible name "Settings") → Settings window.
- Center: `© 2026 Minh Viet`.
- Right: "Help" → styled dialog "Send issues to email vietdau33@gmail.com".

## 7. Settings

Stored in `%AppData%\UsbipdManager\settings.json`; logs in `%LocalAppData%\UsbipdManager\logs\` (daily files, 14 days kept).

| Group | Setting | Default | Effect |
| --- | --- | --- | --- |
| General | Start with Windows | OFF | Creates/removes the elevated logon task (6.1). |
| General | Theme | System | System / Light / Dark. |
| General | Desktop shortcut | OFF | Creates/removes a desktop shortcut; state is read from disk. |
| General | Start Menu shortcut | OFF | Creates/removes a Start Menu shortcut; state is read from disk. |
| Behavior | Restore last mode on startup | ON | See 6.6. |
| Behavior | Auto re-attach in WSL2 mode | ON | Watcher from 6.5. |
| Behavior | WSL distribution | Default distro | Distro kept alive for attaching. |
| Behavior | Tray notifications | ON | Balloon on mode switch and errors. |
| Maintenance | Open log folder | - | Opens the log directory. |

Pinning to the taskbar / Start is not offered: Windows 11 has no public API for unpackaged apps (maintainer chose shortcuts instead).

## 8. Build and versioning

`version.json` is the single source of truth:

```json
{ "version": "1.0.0", "createdDate": null, "updatedDate": null }
```

```powershell
.\build.ps1                       # 1.0.3 → 1.0.4 (bump PATCH), portable exe
.\build.ps1 --version 2           # → 2.0.0 (must be > current)
.\build.ps1 --version 2.2         # → 2.2.0
.\build.ps1 --version 2.2.5       # → 2.2.5
.\build.ps1 --no-bump             # rebuild the current version
.\build.ps1 --install             # also build the installer (alias -i)
.\build.ps1 --release             # release build: first release sets createdDate, later releases set updatedDate
build.bat [same args]             # double-click friendly wrapper
```

Flags combine, e.g. `.\build.ps1 --version 1.1 --release -i`.
Steps: parse and validate → check toolchain → `dotnet test` → `dotnet publish` (self-contained, single-file, win-x64, Release) → optional `ISCC` → copy to `dist/`.
`version.json` is written **only after a successful build**. Version and dates are injected into the assembly (`Version`, `FileVersion`, `InformationalVersion`, `AssemblyMetadata`) and shown in About. Builds before the first release show "Development build" instead of a created date.

Outputs:

- `dist/UsbipdManager-<version>-portable.exe` (always)
- `dist/UsbipdManager-Setup-<version>.exe` (with `--install` / `-i`)

Installer: per-machine to `C:\Program Files\USBIPD Manager\`, Start Menu shortcut, optional desktop shortcut, checkboxes "Start with Windows" and "Launch now", closes the running instance before upgrading, uninstall removes the scheduled task and shortcuts. Signed with the self-signed certificate (Q16).

## 9. UI design approach

- `ui-ux-pro-max` `--design-system` for a developer utility / system tray tool, then `--stack wpf`.
- Tokens (color, typography, spacing, radius) and control styles live in `Theme/` resource dictionaries, swapped at runtime for Light/Dark; views use `DynamicResource` only.
- Console: monospace, timestamped, level tags (`INFO`, `OK`, `WARN`, `ERROR`) plus color (never color alone), auto-scroll, Copy and Clear.
- Device list: description, BusId, VID:PID, state badge, managed switch, "Forget" for disconnected entries.
- Action buttons: the active mode is highlighted; busy state shows progress and disables actions.
- Keyboard accessible, visible focus, 4.5:1 contrast.

## 10. AI Memory

All rewritten in English for this project (the copied files belong to another project):
`CLAUDE.md`, `AGENTS.md`, `docs/ai-memory/{PROJECT_CONTEXT, CODEBASE_MAP, KNOWN_ISSUES, AI_PROMPT_TEMPLATE, README}.md`.

## 11. Execution

- Phase 0 (lead): git init + `.gitignore`, solution and projects, models, interfaces, implementation stubs with fixed constructors (the contract).
- Phase 1 (parallel agents, disjoint folders):
  - A, Core infrastructure: Processes, Usbipd, Wsl + tests.
  - B, Services: Logging, Settings, Services + tests.
  - C, Platform: AutoStart, Shortcuts, SingleInstance, DeviceChangeNotifier, AppInfo.
  - D, UI: design system, theme, views, view models, tray, composition root, icons.
  - E, Build: version.json, build.ps1/.bat, installer script.
- Phase 2 (lead): integrate, build, test, run, verify against this plan, then write AI Memory, README, CLAUDE.md, AGENTS.md.
- Manual verification by the maintainer: reboot autostart without UAC, real phone switching.

## 12. Decisions

| # | Topic | Decision |
| --- | --- | --- |
| Q1 | Which devices are switched | Per-device switch in the main window, remembered by port + device identity, auto ON/OFF on re-plug (6.4). |
| Q2 | App name | USBIPD Manager |
| Q3 | Author | Minh Viet |
| Q4 | Dates in About | `--release` build flag: first release = created date, later releases = updated date. |
| Q5 | Exit | Return everything to Windows; restore the saved mode on next launch. |
| Q6 | Tray menu | Open, Switch to Windows, Switch to WSL2, About, Exit; current mode item disabled. |
| Q7 | usbipd service on Exit | Keep it running. |
| Q8 | Pin to taskbar/Start | Replaced by Desktop / Start Menu shortcut switches. |
| Q9 | Theme | Setting: System (default) / Light / Dark. |
| Q10 | WSL missing | Red error, app unsupported, switch actions disabled. Never auto-install WSL. |
| Q11 | Build outputs | Portable by default; installer with `--install` / `-i`. |
| Q12 | Full `x.y.z` input, `--no-bump` | Both allowed. |
| Q13 | git | git init + `.gitignore`. |
| Q14 | AGENTS.md | Yes. |
| Q15 | Toolchain | Installed by the maintainer. |
| Q16 | Code signing (2026-10-08) | Personal use; Smart App Control turned off by the maintainer. Self-signed certificate, trusted via LocalMachine\Root (`dist/certificate/` for other machines). `build.ps1` signs by default and checks the signing prerequisites before building; `--no-sign` opts out. |
| Q17 | License (2026-10-08) | PolyForm Noncommercial 1.0.0 (`LICENSE`): personal/noncommercial use allowed, commercial use not. |
