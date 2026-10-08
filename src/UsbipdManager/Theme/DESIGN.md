# USBIPD Manager design system

Generated with the `ui-ux-pro-max` skill (v2.13.0) on 2026-10-08, then verified by hand against the product (a compact Windows tray utility for developers).

- `--design-system "developer utility system tray device manager" --density 8`: style **Minimalism & Swiss** (light and dark supported, high contrast, grid, essential). Its "FAQ landing" pattern and indigo palette did not fit a desktop tool and were not used.
- `--design-system "developer tool desktop utility dark"` and `--domain color "developer tool terminal"`: palette **Developer Tool / IDE** ("code dark + run green") and font pairing **JetBrains Mono / IBM Plex Sans**.
- `--stack wpf`: resource dictionaries for tokens, virtualized lists, keyboard navigation, async + Dispatcher for UI updates, pack URIs for resources.
- `--domain ux`: active state shown clearly, confirm risky actions, 4.5:1 text contrast, 2 px focus indicator with 3:1 contrast, loading feedback that does not flicker.

## Tokens

Defined in `Colors.Light.xaml` / `Colors.Dark.xaml` (same keys, swapped at runtime by `ThemeManager`), `Typography.xaml`, `Icons.xaml`. Views only use `DynamicResource`.

| Token | Light | Dark | Use |
| --- | --- | --- | --- |
| Window / Surface / SurfaceAlt | `#F8FAFC` / `#FFFFFF` / `#F1F5F9` | `#0F172A` / `#1B2336` / `#272F42` | background, cards, chips |
| Console | `#F1F5F9` | `#0B1120` | console panel |
| Text / TextMuted | `#0F172A` / `#475569` | `#F8FAFC` / `#94A3B8` | body / secondary |
| Border / BorderStrong | `#E2E8F0` / `#64748B` | `#334155` / `#64748B` | dividers / control edges (>= 3:1) |
| Accent / OnAccent | `#15803D` / `#FFFFFF` | `#22C55E` / `#0F172A` | active mode, switches on, primary |
| Danger / OnDanger | `#DC2626` / `#FFFFFF` | `#EF4444` / `#000000` | destructive confirmation |
| FocusRing | `#1D4ED8` | `#F8FAFC` | keyboard focus |
| Log INFO / OK / WARN / ERROR / $ | `#0F172A` `#166534` `#92400E` `#B91C1C` `#1D4ED8` | `#E2E8F0` `#4ADE80` `#FBBF24` `#F87171` `#7DD3FC` | console levels |

Every text pair was checked: all >= 4.5:1 (lowest: light accent text on white 5.0:1, light WARN on console 6.5:1).

- Typography: Segoe UI Variable Text / Segoe UI (UI) and Cascadia Mono / Consolas (console), the installed Windows equivalents of IBM Plex Sans / JetBrains Mono, so no font files ship. Sizes 12 / 14 / 16 / 20, console 12.5 on an 18 px line.
- Spacing: 4 px grid, dense scale (8, 12, 16). Page margin 16, card padding 12.
- Radius: 4 (badges, chips), 6 (buttons, inputs), 8 (cards, action buttons), 11 (pills, switches).
- Icons: vector `Geometry` on a 24 grid, stroke 2, round caps (Lucide-like), drawn by `Controls/IconView`. No emoji.

## Rules applied

- State is never shown by color alone: console level tags (`INFO`, `OK`, `WARN`, `ERROR`, `$`), text state badges, the "Active" tag on the current mode button, switch thumb position, and tray icons that differ in shape (window panes / `>_` prompt / warning triangle).
- Keyboard: every control can be reached with Tab, has a visible 2 px focus ring, and icon-only buttons have `AutomationProperties.Name` plus a tooltip. Caption buttons are not focusable, as in Windows.
- Feedback: a thin indeterminate bar and status text appear only while a user-started operation runs (not for the controller's background refreshes, so nothing blinks). Switch transitions take 100-120 ms and the final state is set at once.
- Risky actions: switching on an input-like device asks first. Its primary button uses Danger and the safe choice (Cancel) is the default.
- Windows: fixed size, custom title bar, minimize/maximize shown disabled, rounded DWM frame and border color on Windows 11.
