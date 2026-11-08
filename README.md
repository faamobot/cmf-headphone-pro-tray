# CMF Tray

A Windows tray controller for **CMF Headphone Pro**. It controls noise modes, Low Latency (Game)
Mode, Ultra bass, and the headset battery from the taskbar without the Nothing X app. It talks to
the headphones' NT Link Bluetooth control channel.

<!-- Add a screenshot here after your first release: ![screenshot](docs/screenshot.png) -->

> Unofficial. Not affiliated with, or endorsed by, Nothing. Protocol reverse-engineered by the
> community (radiance-project/ear-web, bharadwaj-raju).

## Download & run (for everyone)

1. Go to the [**Releases**](../../releases) page and download the latest `CmfTray.exe`.
2. Double-click it. **Windows SmartScreen** may show a blue "Windows protected your PC" box —
   this is normal for unsigned indie apps. Click **More info → Run anyway**.
3. A tray icon appears (bottom-right, maybe under the `^` arrow).
4. **Left-click** the icon to open the control panel; **right-click** for the menu.

There's nothing to install and no .NET required — the app is self-contained.

### First-time setup
- Your CMF Headphone Pro must be **paired** in Windows (Settings → Bluetooth) and connected to
  this PC. The app finds devices whose Windows name contains “Headphone Pro”.
- Turn on **Start with Windows** from the right-click menu so it launches automatically.

## Features

- **Noise control** — Noise cancellation / Transparency / Off, with live status updates
- **Low Latency Mode (Game Mode)** — on/off with read-back state synchronization
- **Ultra bass** — on/off + level
- **Battery** — one headset battery percentage and icon
- Auto-reconnect when the headphones come back to this PC
- Optional launch at login

## Supported device

This fork targets **CMF Headphone Pro**. Its NT Link channel, device-info session priming, single
battery component, ANC mode payload, and Low Latency command were taken from a hardware capture
and can be checked with the included `--probe` tool. See the [community protocol capture](https://github.com/ncr/omarchy-headphones/blob/main/docs/captures/nothing-headphone-pro.txt).

## Requirements

- Windows 10/11 with Bluetooth

## Build from source (for developers)

Requires the .NET 8 SDK.

```bash
dotnet build -c Release
```

Publish the self-contained single-file exe that ships in Releases:

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

### Diagnostic modes
- `CmfTray.exe --probe` — connects, primes NT Link, and dumps battery/firmware/ANC/latency responses
- The probe log omits the headset serial number and Bluetooth address.
- `CmfTray.exe --anc-map` — maps ANC mode command bytes against your hardware and restores the
  original mode
- `CmfTray.exe --shot` — renders the panel to `flyout-shot.png`

## License

MIT — see [LICENSE](LICENSE).
