# CMF Headphone Pro Tray

An unofficial Windows system-tray controller for **CMF Headphone Pro**. Adjust noise control,
Low Latency (Game) Mode, Ultra bass, and check the headset battery without opening Nothing X.

> This project is independent and is not affiliated with, endorsed by, or supported by Nothing or
> CMF. Bluetooth commands are based on community protocol research. Use at your own discretion.

## What it does

- Switch between Noise Cancellation, Transparency, and Off.
- Toggle Low Latency Mode (Game Mode) and read the device state back.
- Toggle Ultra bass and adjust its level.
- Show one unified battery percentage for the over-ear headset.
- Reflect changes made on the headphones and reconnect when they return to this PC.

This fork currently targets **CMF Headphone Pro**. It discovers paired Windows devices whose name
contains `Headphone Pro` and uses the NT Link Bluetooth control service.

## Install and run

### Option A: download a release

Open [Releases](https://github.com/faamobot/cmf-headphone-pro-tray/releases) and download the
latest `CmfTray.exe` asset. If there are no releases yet, use Option B to build it yourself.

1. Pair CMF Headphone Pro in **Windows Settings → Bluetooth & devices** and connect it to this PC.
2. Run `CmfTray.exe`. The app stays in the notification area; it does not open a regular window.
3. Left-click the headphone tray icon to open the controls. Right-click it for reconnect, device,
   and Start with Windows options.

The published release is self-contained, so it does not require a separate .NET installation.
Windows SmartScreen may warn because community builds are unsigned.

### Option B: build from source

You need **Windows 10 or 11**, Git, and the **.NET 8 SDK**.

```powershell
git clone https://github.com/faamobot/cmf-headphone-pro-tray.git
cd cmf-headphone-pro-tray
dotnet build -c Release
```

Run the tray app:

```powershell
.\bin\Release\net8.0-windows\CmfTray.exe
```

To make a self-contained, single-file executable that can run on a Windows x64 PC without the .NET
runtime installed:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true
```

The published executable is written to
`bin\Release\net8.0-windows\win-x64\publish\CmfTray.exe`.

## Troubleshooting

- Confirm Windows shows the headphones as **Connected** and that their device name includes
  `Headphone Pro`.
- If the app cannot open the control channel, close Nothing X or any other controller that may
  already be using it, then choose **Reconnect** from the tray menu.
- If the panel does not appear, check the hidden notification icons under the taskbar’s `^` arrow.
- The app controls the Bluetooth connection to Windows; it does not replace or configure Windows
  audio output selection.

## Diagnostics

Run these from PowerShell in the folder containing `CmfTray.exe`:

```powershell
.\CmfTray.exe --probe
```

`--probe` reads the headset’s device information, battery, ANC, and latency state. It writes a
`probe-log.txt` transcript beside the executable. The log omits the headset serial number and
Bluetooth address. Include the headset firmware version when reporting a problem.

Other developer diagnostics:

- `CmfTray.exe --anc-map` cycles through ANC modes and attempts to restore the original mode.
- `CmfTray.exe --shot` renders a panel preview to `flyout-shot.png`.

The protocol uses the shared Nothing NT Link service. Headphone Pro command and battery mappings
were cross-checked against this [community hardware capture](https://github.com/ncr/omarchy-headphones/blob/main/docs/captures/nothing-headphone-pro.txt).

## Contributing

Bug reports and improvements are welcome. For Bluetooth issues, include your Windows version,
headphone firmware version, and the redacted `probe-log.txt`. Do not post device serial numbers or
Bluetooth addresses.

## License

MIT. See [LICENSE](LICENSE).
