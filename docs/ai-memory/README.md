# AI Memory: README

- Created: 2026-10-08
- Last updated: 2026-10-08
- Version: 1.0
- Author: AI-generated, reviewed by the maintainer

## Purpose

`docs/ai-memory/` helps a new developer or an AI agent understand USBIPD Manager quickly before reading the code.
It does not replace the code; it is a map that reduces onboarding time and the risk of wrong edits.

`CLAUDE.md` (Claude Code) and `AGENTS.md` (Codex and other agents) at the repo root make agents read these files before every task.
`AGENTS.md` only points to `CLAUDE.md`, so the rules live in one place.

## Files

- `PROJECT_CONTEXT.md`: purpose, stack, runtime model (elevation, tray, autostart task), usbipd-win and WSL facts, data locations, conventions.
- `CODEBASE_MAP.md`: directory tree, entry points, key files per module, tests, build files.
- `KNOWN_ISSUES.md`: open risks and limitations with evidence in the current code or environment. Resolved items are removed.
- `AI_PROMPT_TEMPLATE.md`: a prompt starter with the real project context filled in.
- `../PLAN.md`: the original confirmed specification and the decision log (section 12). Not part of the per-task read list, but the reference for "why".

## How to use `AI_PROMPT_TEMPLATE.md`

1. Copy the template.
2. Fill in `[TASK]`, `[MODULE]`, the files to read first and the related known issues.
3. Paste it into a new AI session.
4. Ask the agent to read the real code of the module before proposing changes.

## Update rules

Who: whoever changes the code (human or AI agent). The maintainer reviews AI-made changes.

When:

- A module, command-line argument, setting or build flag is added or changed.
- A usbipd-win / WSL / Windows behavior is discovered or confirmed.
- A known issue is found, changes severity or is resolved.
- Something in these files no longer matches the code.

How:

- Write only what is confirmed by code or by observed behavior. Mark anything uncertain `[NEEDS CONFIRMATION]`.
- When an issue is resolved, remove it from `KNOWN_ISSUES.md` (history belongs in git, not in the open-issue list).
- When the command-line contract changes, update `PROJECT_CONTEXT.md`, `docs/PLAN.md` 6.1, `App.xaml.cs` and `installer/UsbipdManager.iss` together.
- When the build or versioning changes, update `PROJECT_CONTEXT.md`, `CODEBASE_MAP.md` and the commands in `CLAUDE.md`.
- Bump the `Version` and `Last updated` lines of every file you edit.
- Write in English.

## Checklist after finishing a task

- The root cause was confirmed in the real code.
- Code, tests and docs were updated together; `dotnet test UsbipdManager.slnx` passes.
- Side effects were checked in the callers (AppController → services → clients) and in the UI.
- `KNOWN_ISSUES.md` was updated if an issue was fixed, changed or discovered.
- `PROJECT_CONTEXT.md` / `CODEBASE_MAP.md` were updated if the structure or behavior changed.
