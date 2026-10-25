# MOONLIGHT

[English](README.en.md) | [简体中文](README.md)

A local Windows 11 utility for switching between light and dark mode. Double-click `MOONLIGHT.exe` to switch themes immediately, or set a daily schedule for automatic switching.

The interface defaults to English and can be switched to Simplified Chinese at any time. No installation, administrator privileges, internet connection, or additional runtime downloads are required.

## Usage

1. Place `MOONLIGHT.exe` on your desktop or in another permanent location. Double-click it to switch between light and dark mode; double-click it again to switch back.
2. Right-click the system tray icon and choose `Settings` to open the settings window. You can also double-click the tray icon.
3. Turn on `Enable scheduled switching` to enable the daily schedule. By default, light mode starts at 07:00 and dark mode at 19:00.
4. `Switch Windows theme` controls both the Windows interface and apps that follow the system theme. When unchecked, neither automatic nor manual switching changes the theme.
5. To launch the app when you sign in to Windows, check `Start with Windows` and click `Save and apply`.
6. The language selector sits between `Save and apply` and `Hide to tray`. Choose `English` or `简体中文` to update the interface and tray menu immediately. Your language preference is saved automatically.

Changing the language preserves any unsaved times and checkbox selections. Changes to the schedule or startup settings still require you to click `Save and apply`.

A manual theme selection stays in effect until the next scheduled switch. `Resume schedule` immediately restores the automatic schedule. While the schedule is paused, the manually selected theme remains in effect. If `Switch Windows theme` is off, check it and save before switching themes again.

Closing the settings window minimizes the app to the system tray. Choose `Exit` from the tray menu to quit. The app must remain running for automatic switching to work.

## Interface

The interface is arranged in this order:

- The `MOONLIGHT` name.
- The scheduled switching option.
- The light and dark mode start times.
- The Windows theme and startup options.
- The switch to light, switch to dark, and resume schedule buttons.
- The save and apply button, language selector, and hide to tray button.

The interface and tray menu do not use parenthetical descriptions. The Windows theme option has a tooltip explaining that it also affects app themes.

## Compatibility and data

Designed for Windows 11, using the built-in .NET Framework 4.x. The recorded test environment was Windows 11 26H2, build 26300.

MOONLIGHT supports Windows colors and apps configured to follow the system theme. If an app uses a fixed theme, enable its system theme setting first. Some apps may need to be reopened to refresh their appearance.

The app checks the schedule every 15 seconds. It does not wake a sleeping computer. After the computer wakes or is unlocked, it applies the theme appropriate for the current time. Theme switching is paused while Windows high contrast mode is active.

Settings and rotating logs are stored only on your computer:

```text
%LOCALAPPDATA%\MOONLIGHT\settings.json
%LOCALAPPDATA%\MOONLIGHT\MOONLIGHT.log
```

Settings are saved atomically, with a backup of the previous version. If the settings file is damaged, the app recovers it automatically. On the first upgrade, the app reads the previous version's schedule and migrates its startup entry. Startup is disabled by default for new installations.

Startup registration applies only to the current user. Keep the executable in the same location after enabling it. If you disable the startup entry in Windows Settings or Task Manager, re-enable it there.

Before deleting the app, disable startup and save, then exit from the tray menu and delete `MOONLIGHT.exe`. To remove its settings as well, delete the directory shown above.

## Repository structure

```text
MOONLIGHT/
├── MOONLIGHT.exe
├── README.md
├── README.en.md
├── LICENSE
├── src/
│   ├── Program.cs
│   ├── SettingsForm.cs
│   ├── Localization.cs
│   ├── Core.cs
│   ├── Platform.cs
│   ├── Upgrade.cs
│   ├── app.manifest
│   └── MOONLIGHT.ico
├── tests/Moonlight.Tests.cs
├── scripts/Build.ps1
├── scripts/Test.ps1
├── docs/TESTING.md
└── .github/workflows/build.yml
```

A compiled, ready-to-run executable is included in the repository root. Source code, build scripts, tests, a GitHub Actions workflow, and an MIT license are also included. Personal settings and local logs are excluded from the repository.

## Build and test

Open the repository directory in Windows PowerShell and run:

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

The build uses the C# compiler included with Windows, downloads no packages, and writes the executable to `build\MOONLIGHT.exe`. The default tests cover scheduling logic, settings recovery, language switching, layout order, and upgrade migration without changing the desktop theme.

To test actual theme switching and scheduled transitions at real times, first exit any running instance of MOONLIGHT, then run:

```powershell
.\scripts\Test.ps1 -Integration
```

Integration tests briefly change the desktop theme and restore its original values when finished. Tests use an isolated settings directory. Startup tests use a temporary entry and remove it afterward. Test reports are written to `build\test-results\`.

GitHub Actions builds the app and runs the default tests on a Windows runner, then saves the executable as a build artifact. The workflow uses the official [checkout](https://github.com/actions/checkout) and [upload-artifact](https://github.com/actions/upload-artifact) actions.

## Push to GitHub

Create an empty repository named `MOONLIGHT` on GitHub, extract this project, and run the following commands in the project directory. Replace `YOUR_USERNAME` in the URL with your GitHub username.

```powershell
git init -b main
git add .
git commit -m "Initial MOONLIGHT release"
git remote add origin https://github.com/YOUR_USERNAME/MOONLIGHT.git
git push -u origin main
```

If a remote is already configured, use your existing Git workflow. The project archive does not include local Git history, making it suitable for importing into a new repository.

## License

MIT. See [LICENSE](LICENSE).
