# Claude Code Workspace Instructions

USBIPD Manager is a Windows tray app (C# / WPF / .NET 10) that switches USB devices between Windows and WSL2 through usbipd-win.

## Read before every task

Before any analysis, code change, review or debugging task, read:

1. `docs/ai-memory/PROJECT_CONTEXT.md`: purpose, stack, runtime model, usbipd/WSL facts, conventions
2. `docs/ai-memory/CODEBASE_MAP.md`: where everything lives
3. `docs/ai-memory/KNOWN_ISSUES.md`: open risks and limitations
4. `docs/ai-memory/AI_PROMPT_TEMPLATE.md`: prompt starter
5. `docs/ai-memory/README.md`: how the memory is maintained

`docs/PLAN.md` is the original confirmed specification and the decision log (section 12). When the code and the plan disagree, the code wins. Ask the maintainer before changing a decision recorded there.

## Task routing

| Task touches | Read / edit first |
| --- | --- |
| usbipd commands, device state parsing, device identity | `src/UsbipdManager.Core/Usbipd/`, PROJECT_CONTEXT "usbipd-win facts" |
| WSL detection, distros, keep-alive | `src/UsbipdManager.Core/Wsl/` |
| Managed-device rules (port + device identity, auto bind/attach) | `src/UsbipdManager.Core/Services/DeviceManager.cs`, PLAN 6.4 |
| Mode switching, startup, exit, Init | `Services/ModeSwitcher.cs`, `Services/AppController.cs`, `Services/InitService.cs` |
| Autostart, shortcuts, single instance, device events, version/dates | `src/UsbipdManager.Core/Platform/` |
| Any UI (windows, tray, theme, styles) | `src/UsbipdManager/`, `src/UsbipdManager/Theme/DESIGN.md`; use the `ui-ux-pro-max` skill (`--stack wpf`) |
| Command-line arguments | PLAN 6.1 table, `src/UsbipdManager/App.xaml.cs`, `installer/UsbipdManager.iss` (they must stay in sync) |
| Build, version, release, installer, machine setup | `build.ps1`, `tools/build/BuildTools.psm1`, `installer/UsbipdManager.iss`, `version.json`, `setup.bat` + `tools/setup/Setup.ps1` |
| Code signing, certificate | `tools/signing/`, PROJECT_CONTEXT "Code signing" |
| Contracts between modules | `src/UsbipdManager.Core/Abstractions/`, `src/UsbipdManager.Core/Models/` |

## Commands

```powershell
dotnet build UsbipdManager.slnx
dotnet test UsbipdManager.slnx
.\build.ps1 --dry-run                     # show what a build would do
.\build.ps1                               # bump PATCH, portable exe in dist/
.\build.ps1 --version 1.2 --release -i    # set version, release dates, also build the installer
.\build.ps1 --no-sign                     # builds sign by default; this skips it (no certificate needed)
setup.bat                                 # new machine (admin): installs missing tools after asking (Enter = yes), certificate, builds exe + installer
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build/BuildTools.Tests.ps1
```

The app requires administrator rights (`requireAdministrator` manifest); running it from a non-elevated shell triggers UAC.

## Working rules

- Everything is written in English: code, identifiers, comments, UI text, log messages, docs and AI Memory.
- If a request deviates significantly from the current behavior or from a decision in `docs/PLAN.md`, confirm with the maintainer before changing anything.
- If something is unclear or ambiguous, ask the maintainer before implementing instead of guessing.
- Treat the AI Memory files as required context, not optional reference.
- If the AI Memory conflicts with the code, trust the code, then update the memory after confirming the behavior.
- If information is missing, write `[NEEDS CONFIRMATION]` in the docs instead of guessing.
- Whenever a task reveals durable knowledge (a convention, a usbipd/WSL behavior, a config or build detail, a new risk, a resolved issue), update the relevant file under `docs/ai-memory/` in the same task.
- Add comments only where the code is easy to misunderstand (non-obvious logic, workarounds, hidden rules). Never restate what the code already says.
- Every external command (`usbipd`, `wsl.exe`, `schtasks`, `sc.exe`, `winget`) goes through `IProcessRunner`, so it is logged to the console and can be faked in tests.
- Only `AppController` uses `IOperationGate`; inner services must not take the gate (they call each other and would deadlock).
- Never move devices the user did not switch ON (managed). The exceptions are `usbipd detach --all` on the Windows button and on exit.
- Views use `DynamicResource` tokens from `src/UsbipdManager/Theme/`; no raw colors in views.
- Keep the command-line contract (PLAN 6.1) identical in the app, the installer and the autostart task.
- Add or adjust unit tests in `tests/UsbipdManager.Tests/` for every logic change in `UsbipdManager.Core`.

## Repository focus

- Work in `src/`, `tests/`, `tools/`, `installer/`, `docs/`, and the root build files.
- Ignore generated output: `bin/`, `obj/`, `.artifacts/`, `dist/`.
