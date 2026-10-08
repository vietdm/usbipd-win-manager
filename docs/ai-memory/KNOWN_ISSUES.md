# AI Memory: Known Issues

- Created: 2026-10-08
- Last updated: 2026-10-08
- Version: 1.0
- Author: AI-generated, reviewed by the maintainer

## Scope

Only open issues and limitations backed by the current code or by observed behavior. Remove an item once it is resolved.

## High

1. **Self-signed only: machines must trust the certificate.**
   Builds are signed with a self-signed certificate (PLAN Q16). On a machine without `USBIPD-Manager-CodeSigning.cer` in LocalMachine\Root the app shows "Unknown publisher",
   and Smart App Control (if ON) blocks it: it only accepts Microsoft-trusted certificates, and blocked unsigned builds during development (`0x800711C7`, CodeIntegrity events 3033/3077/3118).
   The maintainer turned Smart App Control off. A Microsoft-trusted certificate (Azure Trusted Signing, CA-issued OV) can be used later through `$env:USBIPD_SIGN_THUMBPRINT`.
   The signed pipeline was tested end to end with a temporary certificate; `[NEEDS CONFIRMATION]` the UAC prompt shows "Verified publisher: Minh Viet" after `New-CodeSigningCert.ps1`.

1a. **Switching a device OFF in WSL2 mode does not work for devices that re-enumerate on detach** (observed with a camera and a phone, 2026-10-08). `DeviceManager.TurnOffAsync` detaches, then `unbind --busid` fails because the busid is briefly gone; the entry stays managed, so the next device event auto-attaches it again. In Windows mode OFF works (no detach). Fix ideas: drop the entry from the managed list before detaching, then unbind with a retry until the busid is back (or by `--guid` from `PersistedGuid`, `[NEEDS CONFIRMATION]` on 5.3.0).
1b. **A hanging background auto-attach blocks user actions for up to 60 s.** Background refreshes hold `IOperationGate`; the attach timeout is 60 s, and a user click (Windows button) waits for it (observed: 17:43:29 attach hung, Windows switch ran 17:44:29 right after the timeout, itself in 0.1 s). Fix ideas: settle delay before auto-attach after a re-enumeration, a shorter auto-attach timeout, and cancelling background work when the user starts an operation.

2. **Not yet verified against a real usbipd-win and real devices.**
   usbipd-win was not installed while the code was written; the `usbipd state` JSON shape, error texts and idempotency rules come from usbipd-win 4.x behavior and are covered by unit tests only.
   Manual checks still needed: Init installing usbipd, bind/attach/detach with a phone, re-plug auto-attach, reboot autostart without UAC, `--exit` during an installer upgrade.

## Medium

3. **WSL state is parsed from English text.** `wsl -l -v` prints localized state names on non-English Windows; "Running" detection would fail there, so `EnsureRunningAsync` would wait 20 s and fail. `[NEEDS CONFIRMATION]` on a localized Windows.
4. **Elevation with another account.** The app runs elevated, so settings, logs, shortcuts and the autostart task belong to the account that elevated. A standard user who elevates with an admin's credentials gets the admin's profile.
5. **Background refresh shares the gate.** A device event that arrives during a long operation (a winget install can take minutes) is applied only after it finishes.

## Low

6. **schtasks output encoding.** With non-ASCII characters in the exe path the queried task command may not match, so the task is re-registered (Info log) on every startup. Harmless.
7. **Single-instance gaps.** A second launch in the few milliseconds between the mutex and the signal events being created does nothing. A launch while the running instance is exiting (returning devices, up to ~15 s) also does nothing: the exiting instance still owns the mutex and ignores Activate, so the new process quits and no instance is left.
8. **Installer upgrade does not remove autostart.** Unticking "Start with Windows" during an upgrade leaves an existing task in place; use the Settings switch.
9a. **setup.bat elevation account.** If a standard user elevates with another administrator account, the certificate is created in that account's CurrentUser store, so a later non-elevated `build.bat` by the standard user finds no certificate. setup.bat itself builds in the same elevated session, so it works. setup.bat has not been run end-to-end on a clean machine yet (winget installs and prompts verified only by code review; the check/build path was verified with `--no-sign --dry-run`).
9. **Size.** The self-contained single-file exe is ~72 MB (installer ~67 MB).
10. **Unverified UI details** (need an elevated interactive run): Windows 11 rounded frame color, tray icon sharpness at 125/150 % scaling, themed tray menu, foreground activation from a second launch, balloons with Focus Assist on, live System theme switching, no focus ring after a click (FocusCues) but a ring after Tab, Help window layout. Reduced-motion is not honored (only a 120 ms switch slide and the busy bar animate).

## By design (not bugs)

- The same managed device on a different port is OFF (maintainer rule, PLAN Q1).
- The Windows button and Exit detach **all** devices (`usbipd detach --all`), including ones attached manually outside the app.
- Pin to taskbar / Start is not offered (no public API for unpackaged apps on Windows 11); Desktop and Start Menu shortcut switches replace it (PLAN Q8).
