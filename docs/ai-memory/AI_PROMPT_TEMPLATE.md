# AI Memory: AI Prompt Template

- Created: 2026-10-08
- Last updated: 2026-10-08
- Version: 1.0
- Author: AI-generated, reviewed by the maintainer

## Quick template

```md
You are helping with USBIPD Manager.

## Project context
- Windows tray app (C# / WPF / .NET 10) that switches USB devices between Windows and WSL2 through usbipd-win.
- The maintainer uses it to debug Flutter apps in WSL2 on Android phones.
- Runs elevated (requireAdministrator); autostart is a Task Scheduler logon task with highest privileges.
- Logic lives in `src/UsbipdManager.Core` (no WPF); the WPF app in `src/UsbipdManager` only talks to `IAppController`.
- Managed devices are matched by port (BusId) AND identity (DeviceKey = VID + serial, PID ignored).
- Everything is written in English.

## Read first
- CLAUDE.md
- docs/ai-memory/PROJECT_CONTEXT.md
- docs/ai-memory/CODEBASE_MAP.md
- docs/ai-memory/KNOWN_ISSUES.md
- [FILES OF THE MODULE]

## Task
[TASK]

## Module
[MODULE: Usbipd | Wsl | Services | Platform | UI | Build | Installer]

## Related known issues
[ISSUE NUMBERS FROM KNOWN_ISSUES.md, or "none"]

## Rules
- Read the real code of the module before proposing changes.
- Ask before deviating from docs/PLAN.md or changing visible behavior.
- Keep every external command behind IProcessRunner; only AppController uses the operation gate.
- Add/adjust unit tests for logic changes; run `dotnet test UsbipdManager.slnx`.
- UI: DynamicResource tokens only; use the ui-ux-pro-max skill with `--stack wpf`.
- Update docs/ai-memory in the same task when you learn something durable.
- Comments only where the code is not obvious.
```

## Ready-made task lines

- Bug in switching: "Devices are not attached after pressing WSL2. Console shows: [paste]. Find the root cause in ModeSwitcher/DeviceManager/UsbipdClient."
- New setting: "Add a setting [NAME] with default [VALUE] that [EFFECT]. Follow CODEBASE_MAP 'A new setting'."
- Release: "Prepare release [VERSION]: run `.\build.ps1 --version [VERSION] --release -i`, then update the AI Memory dates."
