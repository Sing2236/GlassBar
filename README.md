# GlassBar

A translucent, animated Windows taskbar overlay. GlassBar leaves Explorer running and only hides the native taskbar window while GlassBar is open. Closing GlassBar restores it immediately.

[Website](https://get-glassbar.vercel.app) · [Download](https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe) · [GlassBar Pro — $5 once](https://www.paypal.com/cgi-bin/webscr?cmd=_xclick&business=Ethanhuynh365%40gmail.com&item_name=GlassBar+Pro+Lifetime&amount=5.00&currency_code=USD&no_shipping=1) · [Support](https://www.paypal.com/cgi-bin/webscr?cmd=_donations&business=Ethanhuynh365%40gmail.com&currency_code=USD)

## Preview

The animation below is captured from the real app, moving from **Aurora** into **Rain**.

![GlassBar running the Aurora and Rain effects](docs/glassbar-effects.gif)

## Install

Download [GlassBarSetup.exe](https://github.com/Sing2236/GlassBar/releases/latest/download/GlassBarSetup.exe), or use the portable `GlassBar.exe` from the latest release.

The installer is per-user, needs no administrator access, and offers to start GlassBar automatically when you sign in.

Installed builds check for updates shortly after launch and every four hours. Updates from `main` are downloaded from GitHub Releases, verified against the release SHA-256 manifest, installed silently, and relaunched. Recovery launches using `--safe` never update automatically.

## Run from source

```powershell
dotnet run --project .\GlassBar.csproj
```

## Build a standalone app

```powershell
dotnet publish .\GlassBar.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o .\release
```

The standalone executable will be `release\GlassBar.exe`.

Every push to `main` runs [the release workflow](.github/workflows/release-main.yml). It produces a versioned Windows executable, installer, and `update.json` checksum manifest, then marks that GitHub release as latest so installed copies can update.

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

**Rain** and **Aurora** are included free. **Snow**, **Fireflies**, **Pulse**, and the **Custom Effect Lab** are unlocked by a one-time $5 GlassBar Pro purchase. **Off** remains available to everyone as a safety and accessibility control. Preferences are kept in `%LOCALAPPDATA%\GlassBar\settings.json`.

The Custom Effect Lab lets users combine four particle shapes with floating, falling, rising, or drifting motion. Density, speed, size, glow, trail strength, primary accent, and secondary color are editable live. Custom effects can be exported as `.glassfx.json` files and imported on another computer.

GIF stickers are copied into `%LOCALAPPDATA%\GlassBar\Stickers`. Their selected size, opacity, and dragged position are restored after GlassBar restarts.

## GlassBar Pro licenses

Buy a lifetime license with [PayPal for $5](https://www.paypal.com/cgi-bin/webscr?cmd=_xclick&business=Ethanhuynh365%40gmail.com&item_name=GlassBar+Pro+Lifetime&amount=5.00&currency_code=USD&no_shipping=1). After the payment is manually verified, the signed key is emailed to the buyer's PayPal email and works offline on future launches.

To fulfill a verified payment, the project owner runs:

```powershell
.\scripts\fulfill-license.ps1 `
  -Email "buyer@example.com" `
  -PayPalTransactionId "PAYPAL-TRANSACTION-ID" `
  -PaymentVerified
```

The fulfillment command refuses to send until `-PaymentVerified` is supplied, emails the signed key over SMTP, and records the transaction locally to prevent accidental duplicate fulfillment. It defaults to Gmail SMTP and prompts securely for an app password. Gmail requires 2-Step Verification before you can [create an app password](https://support.google.com/accounts/answer/185833). Set `GLASSBAR_SMTP_HOST`, `GLASSBAR_SMTP_PORT`, `GLASSBAR_SMTP_USER`, and `GLASSBAR_SMTP_FROM` to use another provider. For unattended use, `GLASSBAR_SMTP_PASSWORD` is supported, but an interactive secure prompt is safer.

Preview an email without sending or recording it with `-DryRun`.

The private signing key lives at `%USERPROFILE%\.glassbar\license-private.pem`; keep it private, back it up securely, and never commit it. Only the public verification key is included in the app. Because GlassBar is open source, someone who recompiles modified source can bypass client-side feature checks; signed licenses protect official builds, not altered forks.

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
