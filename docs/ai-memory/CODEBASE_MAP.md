# AI Memory: Codebase Map

- Created: 2026-10-08
- Last updated: 2026-10-08
- Version: 1.0
- Author: AI-generated, reviewed by the maintainer

## 1. Tree

```
usbipd-win-manager/
├── CLAUDE.md, AGENTS.md            # Agent instructions (AGENTS.md points to CLAUDE.md)
├── README.md                       # User + developer quick start
├── LICENSE                         # PolyForm Noncommercial 1.0.0 (personal/noncommercial use only)
├── UsbipdManager.slnx              # Solution
├── Directory.Build.props           # Version/dates from version.json, product metadata, shared compiler settings
├── version.json                    # { version, createdDate, updatedDate }
├── build.ps1, build.bat            # Build pipeline (portable exe, optional installer)
├── .gitignore, .gitattributes      # .bat/.ps1/.iss forced to CRLF
├── installer/UsbipdManager.iss     # Inno Setup script
├── tools/
│   ├── build/BuildTools.psm1       # Arg parsing, version rules, date rules, version.json I/O
│   ├── build/BuildTools.Tests.ps1  # Plain-assert tests for the module (+ signing helpers)
│   ├── signing/Signing.psm1        # Certificate lookup, preflight, signtool/Set-AuthenticodeSignature, export
│   ├── signing/New-CodeSigningCert.ps1  # One-time (admin): create/trust/export the self-signed certificate
│   ├── signing/Sign-File.ps1       # CLI signer; Inno Setup's sign tool
│   ├── signing/install-certificate.bat  # Copied to dist/certificate/; trusts the .cer on other machines
│   └── generate-icons.ps1          # Renders Assets/*.ico with System.Drawing
├── src/
│   ├── UsbipdManager.Core/         # All logic, no WPF
│   │   ├── Abstractions/           # Interfaces = module contracts
│   │   ├── Models/                 # Records/enums shared by all layers
│   │   ├── Processes/              # ProcessRunner, ManagedProcess, JobObject, CommandLineFormatter
│   │   ├── Usbipd/                 # UsbipdClient, UsbipdStateParser, DeviceKeys, DeviceClassifier,
│   │   │                           # UsbipdServiceController, UsbipdInstaller, UsbipdErrors, ExecutableLocator
│   │   ├── Wsl/                    # WslClient (+ keep-alive), WslListParser
│   │   ├── Logging/                # LogService, LogServiceExtensions
│   │   ├── Settings/               # SettingsStore
│   │   ├── Services/               # AppController, EnvironmentChecker, InitService, DeviceManager,
│   │   │                           # ModeSwitcher, OperationGate, DeviceText, NaturalStringComparer
│   │   └── Platform/               # AutoStartManager, TaskSchedulerXml, ShortcutManager, ShellLink,
│   │                               # SingleInstance, DeviceChangeNotifier, AppInfo, Elevation
│   └── UsbipdManager/              # WPF exe
│       ├── app.manifest            # requireAdministrator, PerMonitorV2
│       ├── App.xaml(.cs)           # Composition root, CLI handling, lifetime
│       ├── Startup/                # CommandLineOptions
│       ├── Views/                  # MainWindow, SettingsWindow, AboutWindow, MessageDialog
│       ├── ViewModels/             # Main, Console, DeviceItem, Settings, About
│       ├── Tray/                   # TrayIconService (NotifyIcon + menu), TrayMenuRenderer (themed menu)
│       ├── Theme/                  # Colors.Light/Dark, Typography, Icons, Controls, ThemeManager, DESIGN.md
│       ├── Controls/               # IconView, UiProps, UiChrome, Converters
│       ├── Services/               # IDialogService, DialogService
│       ├── Mvvm/                   # ObservableObject, RelayCommand (+ async)
│       ├── Interop/                # NativeMethods (window frame, foreground)
│       └── Assets/                 # app.ico, tray-windows.ico, tray-wsl.ico, tray-warning.ico
├── tests/UsbipdManager.Tests/      # xUnit, mirrors the Core folders
│   ├── TestDoubles/                # TestLog, FakeProcessRunner (shared)
│   └── Services/Fakes/             # Fakes for every Core interface
└── docs/
    ├── PLAN.md                     # Confirmed spec + decision log
    └── ai-memory/                  # This memory
```

Generated, ignored: `bin/`, `obj/`, `.artifacts/` (build outputs, `.artifacts/publish/<v>/`), `dist/` (final exe files, `dist/certificate/`), `*.pfx`.

## 2. Entry points

| Entry | File |
| --- | --- |
| App startup, CLI, object graph, exit | `src/UsbipdManager/App.xaml.cs` |
| UI → logic boundary | `src/UsbipdManager.Core/Abstractions/IAppController.cs`, `Services/AppController.cs` |
| Main window logic | `src/UsbipdManager/ViewModels/MainViewModel.cs` |
| Tray menu | `src/UsbipdManager/Tray/TrayIconService.cs` |
| Build | `build.ps1` → `tools/build/BuildTools.psm1` |
| Installer | `installer/UsbipdManager.iss` |

## 3. Where to change what

| Change | Files |
| --- | --- |
| A usbipd command or its error handling | `Core/Usbipd/UsbipdClient.cs`, `UsbipdErrors.cs`, tests in `tests/.../Usbipd/` |
| Device identity rule | `Core/Usbipd/DeviceKeys.cs` (+ `DeviceKeysTests`), then the DeviceManager tests |
| Managed-device behavior | `Core/Services/DeviceManager.cs` (+ `DeviceManagerTests`, incl. the maintainer scenario) |
| Startup / restore / shutdown order | `Core/Services/AppController.cs` (+ `AppControllerTests`) |
| Console wording | the service that logs it; device wording in `Core/Services/DeviceText.cs` |
| A new setting | `Core/Models/AppSettings.cs` → `ViewModels/SettingsViewModel.cs` → `Views/SettingsWindow.xaml` → consumer |
| Colors / styles | `Theme/Colors.*.xaml` (keep both files' keys identical), `Theme/Controls.xaml`, `Theme/DESIGN.md` |
| Icons | `tools/generate-icons.ps1`, re-run it, commit `Assets/*.ico` |
| A CLI argument | `Startup/CommandLineOptions.cs`, `App.xaml.cs`, `installer/UsbipdManager.iss`, PLAN 6.1, PROJECT_CONTEXT 3 |
| Version / build flags | `tools/build/BuildTools.psm1` (+ its tests), `build.ps1`, CLAUDE.md commands |
| Code signing | `tools/signing/Signing.psm1`, `build.ps1` (preflight, sign step, ISCC `/S`), `installer/UsbipdManager.iss` (`SignToolName`) |

## 4. Config and data

- `version.json` and `Directory.Build.props`: version and dates baked into the assembly (`AssemblyInformationalVersion`, `AssemblyMetadata("CreatedDate"/"UpdatedDate")`), read by `Platform/AppInfo.cs`.
- Runtime data paths: PROJECT_CONTEXT section 8.
- No environment variables or secrets are used.
