# GlassBar

A translucent, animated Windows taskbar overlay. GlassBar leaves Explorer running and only hides the native taskbar window while GlassBar is open. Closing GlassBar restores it immediately.

## Run it

```powershell
dotnet run --project .\glassbar\GlassBar.csproj
```

## Build a standalone app

```powershell
dotnet publish .\glassbar\GlassBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\glassbar\release
```

The standalone executable will be `glassbar\release\GlassBar.exe`.

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
- Gear: effects, opacity, safe taskbar toggle, and exit

Choose **Rain**, **Aurora**, or **Off** in settings. Preferences are kept in `%LOCALAPPDATA%\GlassBar\settings.json`.

For UI development without hiding the Windows taskbar, add `--safe`:

```powershell
dotnet run --project .\glassbar\GlassBar.csproj -- --safe
```
