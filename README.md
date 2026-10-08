# USBIPD Manager

A Windows tray app that moves USB devices between **Windows** and **WSL2** with one click, built on [usbipd-win](https://github.com/dorssel/usbipd-win).
Made for debugging Flutter/Android apps inside WSL2 on a real phone while keeping the phone usable from Windows.

## Features

- Lives in the system tray and starts with Windows **elevated, without a UAC prompt** (Task Scheduler logon task).
- **Init** checks and fixes the environment: installs usbipd-win with winget, starts its service, shares the selected devices.
- **Windows** / **WSL2** buttons move the devices you switched on; the choice is remembered.
- Per-device switches remembered by **port + device identity**: plug the same phone into the same port and it follows the mode again automatically; another device on that port is left alone.
- Auto re-attach after a re-plug in WSL2 mode. Exit returns everything to Windows and restores the mode at next start.
- Console log of every check and command, light/dark/system theme, desktop and Start Menu shortcut switches.

## Requirements

- Windows 10/11 x64 with **WSL 2** and at least one WSL 2 distribution (the app reports the machine as unsupported otherwise).
- Administrator rights.
- usbipd-win is installed by the app (Init) if missing.

## Install

- **Installer**: run `UsbipdManager-Setup-<version>.exe`, keep "Start with Windows" ticked.
- **Portable**: run `UsbipdManager-<version>-portable.exe` from any folder; turn on "Start with Windows" in Settings.

The binaries are signed with a self-signed certificate. On a new machine, first copy `dist\certificate\` (`USBIPD-Manager-CodeSigning.cer` + `install-certificate.bat`) and run `install-certificate.bat`, otherwise Windows shows "Unknown publisher" (and Smart App Control, if on, blocks the app).

## Usage

1. Double-click the tray icon to open the window.
2. Press **Init** once (it turns into **Refresh** when everything is ready).
3. Turn on the switch of your phone in the device list.
4. Press **WSL2** to give it to WSL (`adb devices` inside WSL now sees it) or **Windows** to give it back.

Right-click the tray icon for Open, Switch to Windows, Switch to WSL2, About and Exit. **Help** in the window footer answers common problems (for example adb "no permissions" in WSL). Issues: vietdau33@gmail.com.

## Development

Requirements: .NET SDK 10, Inno Setup 6 (only for the installer), Windows PowerShell 5.1+, Windows SDK signtool (optional, for signing).

Builds are signed by default. One-time setup, from an administrator PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1                       # create + trust the certificate
powershell -ExecutionPolicy Bypass -File tools\signing\New-CodeSigningCert.ps1 -PfxPath D:\backup.pfx  # same, plus a private-key backup
```

```powershell
dotnet build UsbipdManager.slnx
dotnet test UsbipdManager.slnx

.\build.ps1 --dry-run                     # show what would happen
.\build.ps1                               # bump PATCH (1.0.3 -> 1.0.4), build dist\UsbipdManager-<v>-portable.exe
.\build.ps1 --version 2                   # 2.0.0 (must be greater than the current version); 2.2 -> 2.2.0; 2.2.5 allowed
.\build.ps1 --no-bump                     # rebuild the current version
.\build.ps1 -i                            # also build dist\UsbipdManager-Setup-<v>.exe
.\build.ps1 --release                     # first release sets the created date, later releases the updated date
.\build.ps1 --no-sign                     # skip signing (no certificate needed)
build.bat --version 1.1 --release -i      # same flags, double-click friendly
```

The version lives in `version.json` and is only written after a successful build.

Project documentation for developers and AI agents: [CLAUDE.md](CLAUDE.md), [docs/ai-memory/](docs/ai-memory/), [docs/PLAN.md](docs/PLAN.md).

## License

© 2026 Minh Viet. Licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE): free for personal and other noncommercial use; commercial use is not permitted.

usbipd-win, which the app installs and calls, is a separate project with its own license (GPL-3.0).
