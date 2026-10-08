# AI Memory: Project Context

- Created: 2026-10-08
- Last updated: 2026-10-08
- Version: 1.0
- Author: AI-generated, reviewed by the maintainer

## 1. Overview

USBIPD Manager is a Windows tray app (like Unikey) that moves USB devices between Windows and WSL2 with one click.
The maintainer (Minh Viet) builds Flutter apps inside WSL2 and debugs them on physical Android phones. WSL2 has no native USB access;
[usbipd-win](https://github.com/dorssel/usbipd-win) shares devices over USB/IP, but a device belongs to only one side at a time. The app automates the switching.

The confirmed specification and decision log: `docs/PLAN.md` (section 12 lists every maintainer decision).

## 2. Stack

| Concern | Choice |
| --- | --- |
| Language / UI | C# (latest), WPF, .NET 10 (`net10.0-windows`) |
| Projects | `UsbipdManager.Core` (class library, no WPF, all logic), `UsbipdManager` (WPF exe), `UsbipdManager.Tests` (xUnit) |
| Solution | `UsbipdManager.slnx` |
| MVVM | Own lightweight `ObservableObject` / `RelayCommand` / async command in `src/UsbipdManager/Mvvm/` (no NuGet MVVM package) |
| Tray | `System.Windows.Forms.NotifyIcon` (`UseWindowsForms=true`; WinForms usings are removed from implicit usings on purpose) |
| Device events | WMI `Win32_DeviceChangeEvent` (`System.Management`) + 30 s periodic refresh |
| Service control | `System.ServiceProcess.ServiceController` + `sc.exe` |
| Packaging | `dotnet publish` self-contained single-file win-x64 (~72 MB) |
| Installer | Inno Setup 6 (`installer/UsbipdManager.iss`) |
| Build script | `build.ps1` (Windows PowerShell 5.1 compatible) + `build.bat` wrapper |

NuGet packages: `System.Management` 10.0.0, `System.ServiceProcess.ServiceController` 10.0.0 (Core); xUnit stack (tests). Nothing else.

## 3. Runtime model

- `app.manifest` → `requireAdministrator`. Every manual launch shows UAC; all usbipd operations need admin anyway.
- Autostart = Task Scheduler task `USBIPD Manager`: logon trigger (current user, 5 s delay), `RunLevel=HighestAvailable`, action `UsbipdManager.exe --tray`.
  This is the only supported way to start elevated at logon without a UAC prompt (`HKCU\...\Run` cannot do it).
  On every startup `EnsurePathUpToDateAsync` re-registers the task if it points to another exe path (portable exe moved). A disabled task is never re-enabled.
- Single instance: named mutex `Local\UsbipdManager.Mutex` + auto-reset events `Local\UsbipdManager.Activate` / `.Exit`.
- `ShutdownMode=OnExplicitShutdown`. The X button hides the main window to the tray; minimize/maximize are shown disabled (custom `WindowChrome` title bar).
- Exit (tray menu or `--exit` signal): `AppController.ShutdownAsync` (15 s budget) → `usbipd detach --all` + stop the WSL keep-alive, **without** changing the saved mode → dispose → quit.
  Next launch restores WSL2 mode when `RestoreLastModeOnStartup` is on. The usbipd Windows service is never stopped.
- Logoff/shutdown (`SessionEnding`): the same release with a 4 s budget.

### Command-line contract (keep identical in `App.xaml.cs`, `Startup/CommandLineOptions.cs`, `installer/UsbipdManager.iss`, PLAN 6.1)

| Argument | Behavior | Exit code |
| --- | --- | --- |
| (none) | Normal start, window shown | - |
| `--tray` | Start hidden in the tray (logon task) | - |
| `--register-autostart` | Headless: create/refresh the logon task for this exe | 0 / 1 |
| `--unregister-autostart` | Headless: delete the logon task | 0 / 1 |
| `--exit` | Headless: signal the running instance to release devices and quit; wait up to 20 s | 0 (also when nothing runs) / 1 timeout |

## 4. Core flows

- **Layering**: UI → `IAppController` (the only class using `IOperationGate`) → `EnvironmentChecker`, `InitService`, `DeviceManager`, `ModeSwitcher` → `UsbipdClient`, `WslClient`, `UsbipdServiceController`, `UsbipdInstaller` → `IProcessRunner`.
  Inner services are not gated; taking the gate inside them would deadlock.
- **Startup** (`AppController.StartAsync`): environment checks → start device monitoring → decide the mode (restore WSL2 if allowed and possible; if restore is off save Windows; if restore is on but switching is not possible yet, keep WSL2 and log a warning) → device refresh.
- **Background refresh**: device events debounced 1.5 s + every 30 s while usbipd is ready, through `TryRunAsync`; a refresh skipped because the gate was busy runs right after. `IsBusy` therefore flips briefly; the UI's busy state counts only user-started operations.
- **Init**: checks → (unsupported → stop) → winget install usbipd-win if missing → service Disabled → Automatic → start service → bind connected managed devices → checks again.
- **Windows button**: `usbipd detach --all` (all devices, managed or not), stop keep-alive, save mode Windows.
- **WSL2 button**: ensure a WSL2 distro runs (keep-alive), save mode WSL2, attach connected managed devices. No managed device connected → warning, mode stays WSL2 so later plugs auto-attach.
- **Unsupported machine**: no WSL or no WSL2 distro → red banner, Windows/WSL2/toggles/tray switch items disabled; Init stays enabled to re-check. WSL is never installed automatically.

## 5. Managed devices (maintainer rule, PLAN 6.4)

- A managed entry = `BusId` (port) + `DeviceKey` (identity) + description, stored in settings.
- `DeviceKey` = `VID_xxxx\SERIAL` when the instance ID ends with a real serial (no `&`), PID ignored because Android changes its PID with the USB mode (MTP/PTP/ADB); otherwise the full upper-cased instance ID (Windows-generated, port-based).
- ON = same port AND same identity. Another device on that port is OFF and untouched; the same device on another port is OFF.
- Turning ON: re-reads the state (refuses if another device is now on the port), `usbipd bind`, remembers, attaches immediately in WSL2 mode. Input-like devices (keyboard, mouse, HID, Bluetooth, ...) are confirmed in the UI first.
- Turning OFF: detach if attached, `usbipd unbind`, forget. If detach/unbind fails the device stays managed.
- Re-plug of a managed device on its port → auto bind, and auto attach in WSL2 mode with "Auto re-attach" on. Failed automatic attempts back off 30 s per device; unplugging clears the back-off.
- Unmanaged devices are never bound/unbound by the app.

## 6. usbipd-win facts used by the code

- Executable: `%ProgramFiles%\usbipd-win\usbipd.exe`, else the PATH re-read from the registry (it can be installed while the app runs).
- `usbipd state` prints JSON `{"Devices":[{"BusId","ClientIPAddress","Description","InstanceId","IsForced","PersistedGuid","StubInstanceId"}]}`.
  `BusId` null = bound but unplugged. State: `ClientIPAddress` set → Attached; `PersistedGuid` set → Shared; else Not shared.
- `bind`/`unbind`/`attach --wsl`/`detach --busid`/`detach --all`. Errors are read from the `usbipd: error:` line. "already ..." (bind/attach) and "not attached"/"not shared" (detach/unbind) count as success.
- `attach --wsl` needs a running WSL2 distro, hence the keep-alive.
- `bind` is persisted by usbipd per device instance; a bound device stays "Shared" on any port.
- Install: `winget install --id dorssel.usbipd-win -e --silent ...`; exit codes 0, `0x8A15002B` (already installed) and `0x8A150109` (reboot required) count as success.
- Service name `usbipd`. Starting a Disabled service fails, so Init sets it to Automatic first.
- Only read-only commands (`state`, `--version`) are not echoed to the console; every state-changing command is logged as `$ ...`.

## 7. WSL facts used by the code

- `%SystemRoot%\System32\wsl.exe`, run with `WSL_UTF8=1`; UTF-16LE output is still handled (older WSL ignores the variable).
- Installed = wsl.exe exists AND `wsl --status` exits 0 (the stub exe exists on machines without the feature).
- Distros from `wsl -l -v` (`*` = default, last column = version, state "Running").
- Keep-alive: hidden `wsl.exe -d <distro> --exec sleep infinity`, owned by an app-wide Job Object with kill-on-close, so it dies with the app. Started for WSL2 mode, stopped for Windows mode and on exit.
- Distro choice: the setting, else the default; if the default is WSL1/missing, the first WSL2 distro.

## 8. Data locations

| Data | Path |
| --- | --- |
| Settings | `%AppData%\UsbipdManager\settings.json` (camelCase JSON, enums as strings; corrupt file → renamed `.corrupt`, defaults used) |
| Logs | `%LocalAppData%\UsbipdManager\logs\usbipd-manager-yyyyMMdd.log`, 14 days kept, line `yyyy-MM-dd HH:mm:ss.fff [LEVEL  ] message` |
| Install dir | `C:\Program Files\USBIPD Manager\UsbipdManager.exe` (installer) or anywhere (portable) |
| Shortcuts | `USBIPD Manager.lnk` on the Desktop / in Start Menu Programs (current user and all users); the installer's uninstaller deletes this exact name |
| Autostart | Task Scheduler task `USBIPD Manager` |

The app runs elevated, so these are the folders of the user who elevated.

## 9. Build and versioning

- `version.json` = `{ version, createdDate, updatedDate }`, the single source of truth. `Directory.Build.props` reads it for normal builds; `build.ps1` passes `/p:Version`, `/p:AppCreatedDate`, `/p:AppUpdatedDate`.
- `build.ps1` flags: `--version/-v <x|x.y|x.y.z>` (must be greater), `--no-bump`, `--install/-i`, `--release/-r`, `--skip-tests`, `--dry-run`, `--help/-h`. Default bumps PATCH.
- `--release`: first release sets `createdDate`, later releases set `updatedDate`. About shows "Development build" until the first release.
- Steps: tests → publish → `dist/UsbipdManager-<v>-portable.exe` → (installer) `dist/UsbipdManager-Setup-<v>.exe` → write `version.json` only if everything succeeded.
- PowerShell 5.1 quirks handled in `BuildTools.psm1`: a bare `--` is swallowed when calling `.ps1` directly; numeric tokens like `1.10` arrive as numbers and are re-read as typed text.

## 10. UI design system

- From the `ui-ux-pro-max` skill: "Minimalism & Swiss" style with the "Developer Tool / IDE" palette (slate surfaces, green accent), Segoe UI Variable + Cascadia Mono/Consolas, 4 px grid, radii 4/6/8/11. Details and contrast checks: `src/UsbipdManager/Theme/DESIGN.md`.
- Light and dark dictionaries share the same keys; `ThemeManager` applies System/Light/Dark live (System follows `AppsUseLightTheme`).
- Views use `DynamicResource` for every color/brush; `StaticResource` only for converters and `BasedOn`.

## 11. Conventions

- English everywhere (code, UI, logs, docs).
- File-scoped namespaces, sealed classes, records for data, nullable enabled.
- Comments only for non-obvious logic, workarounds or hidden rules.
- Every external command goes through `IProcessRunner`. Tests use `FakeProcessRunner`, `TestLog` and the fakes in `tests/UsbipdManager.Tests/Services/Fakes/`.
- Public constructors are the composition contract (see `App.xaml.cs`); testability seams are `internal` constructors (`InternalsVisibleTo UsbipdManager.Tests`).
- Log messages are user-facing: short, actionable, consistent wording.
