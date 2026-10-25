# Validation record

- Date: 2026-10-25, Asia/Shanghai.
- Platform: Windows 11 26H2, build 26300, built-in .NET Framework.
- Version: 1.1.0.0.
- Portable executable: `MOONLIGHT.exe`, 50,688 bytes.
- Compiler: Windows built-in C# compiler, C# 5-compatible sources, warnings treated as errors.
- Final suite: **29 of 29 passed** from 19:40:32 to 19:41:06.
- Executable SHA-256: `D3AE9112FC0B49A6A4DD64C6DB2885C1B12B215AC1B3DCE38BE990699873667F`.

## Verified behavior

- Exact MOONLIGHT branding, English defaults, and a complete Simplified Chinese translation catalog.
- No parentheses in either UI/tray translation catalog.
- Only three settings checkboxes: schedule, combined Windows theme, and startup.
- Requested top-to-bottom layout, with the language selector between Save and apply and Hide to tray.
- No former introductory subtitle, theme summary card, or local-time footer.
- Default window content fits without scrolling at the tested layout size.
- Immediate language selector events in both directions; unsaved times and checkbox changes remain intact.
- Language preference persistence without resetting the schedule or a manual theme override.
- Double launch toggles both ways, forwards to one tray process, and leaves Settings hidden.
- Combined theme option changes both Windows and application theme settings; its disabled state blocks both automatic and manual changes.
- Actual Windows personalization values and notification broadcast, plus unchanged-value no-op behavior.
- Live scheduled transition: Real boundary 19:41:00; dark applied 6.5 s later. No clock manipulation.
- Full-day schedule invariants, cross-midnight schedules, exact boundaries, and next-day events.
- Pause behavior, manual override persistence and expiry, high-contrast guard logic, and catch-up after a skipped interval.
- Atomic settings writes, backup recovery, damaged-file preservation, and failed-save handling.
- Per-user startup registration round trip, with a temporary test entry removed afterward.
- A responsive native Settings window.

## Exact desktop upgrade check

The final executable was copied to the desktop as `MOONLIGHT.exe` and launched twice with no arguments. Windows.UI.ViewManagement.UISettings reported background `#FFFFFFFF` after the first launch and `#FF000000` after the second. The previous schedule was retained, the default language was English, and the previous startup registration was moved to MOONLIGHT. The theme values were restored after the check.

The earlier executable was kept in a local upgrade backup and is excluded from this repository.

## Scope limits

The desktop automation connection could not initialize because it rejected the WSL working-directory URI. Bilingual UI structure and selector events were tested in process; external mouse-click automation and screenshot appearance checks were unavailable. The real Settings window completed a startup and responsiveness smoke test.

High-contrast, wake, and unlock decision paths were checked without actually enabling high contrast, suspending, or locking the computer. Startup registration was verified without rebooting. The GitHub Actions workflow is included but has not been executed on GitHub during this local session.

See `test-results.json` for individual results. Run `scripts/Build.ps1` followed by `scripts/Test.ps1 -Integration` to repeat the suite on Windows. Integration tests temporarily change the desktop theme and restore it on exit.
