# GlassBar

A translucent, animated Windows taskbar overlay. GlassBar leaves Explorer running and only hides the native taskbar window while GlassBar is open. Closing GlassBar restores it immediately.

## Install

Download [GlassBarSetup.exe](https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe), or use the portable `GlassBar.exe` from the latest release.

The installer is per-user, needs no administrator access, and offers to start GlassBar automatically when you sign in.

## Run from source

```powershell
dotnet run --project .\GlassBar.csproj
```

## Build a standalone app

```powershell
dotnet publish .\GlassBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\release
```

The standalone executable will be `release\GlassBar.exe`.

## Safety

- Explorer is never stopped, replaced, patched, or removed.
- The original taskbar is restored on exit and if GlassBar catches an error.
- A separate watchdog restores it even if GlassBar is force-killed.
- Press **Ctrl + Alt + Shift + T** at any time for an emergency restore and exit.
- If the process is forcibly killed, restarting Explorer restores its taskbar.

## Controls

- Windows logo: Start
- Magnifier: Windows Search
- Overlapping windows: Task View
- Folder / terminal: pinned shortcuts
- Running icons: activate their windows
- Network / volume: Quick Settings
- Clock: notifications and calendar
- Gear: effects, opacity, size, startup, GIF stickers, safe taskbar toggle, and exit

Choose **Rain**, **Aurora**, or **Off** in settings. Preferences are kept in `%LOCALAPPDATA%\GlassBar\settings.json`.

For UI development without hiding the Windows taskbar, add `--safe`:

```powershell
dotnet run --project .\GlassBar.csproj -- --safe
```

## Website

The download page lives in `website/` and is deployed with Vercel. Run its tests with:

```powershell
cd .\website
npm install
npx playwright install chromium
npm test
```

## Support

If GlassBar is useful to you, you can [support its development with PayPal](https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=Ethanhuynh365%40gmail.com&currency_code=USD).
