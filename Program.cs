using System.IO;
using System.Runtime.InteropServices;

namespace CmfTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--probe"))
            return Probe(args);

        if (args.Contains("--anc-map"))
            return AncMap(args);

        if (args.Contains("--shot"))
            return Shot();

        if (args.Contains("--icons"))
            return IconsPreview();

        if (args.Contains("--svgicon"))
            return SvgIconPreview();

        // Single instance: if one is already running, just exit quietly.
        using var mutex = new System.Threading.Mutex(true, @"Global\CmfTray_SingleInstance", out bool isNew);
        if (!isNew) return 0;

        // Log any startup/runtime crash so a failed login-launch isn't silent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        // Default: system tray app. WPF hosts the flyout window; a WinForms NotifyIcon
        // provides the tray icon. Run the WPF message loop with no main window.
        System.Windows.Forms.Application.EnableVisualStyles();
        var wpf = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        wpf.DispatcherUnhandledException += (_, e) => { LogCrash(e.Exception); e.Handled = true; };

        try
        {
            using var app = new TrayApp(wpf);
            wpf.Run();
            return 0;
        }
        catch (Exception ex)
        {
            LogCrash(ex);
            return 1;
        }
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            File.AppendAllText(Path.Combine(dir, "startup-error.log"), $"{DateTime.Now:s}  {ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }

    /// <summary>Preview the SVG-based tray icon at real sizes on dark + light bars.</summary>
    private static int SvgIconPreview()
    {
        using var connected = IconFactory.MakeFromSvg(true, 64);
        using var disc = IconFactory.MakeFromSvg(false, 64);
        using var cb = connected.ToBitmap();
        using var db = disc.ToBitmap();

        int zoom = 128, pad = 24;
        using var bmp = new System.Drawing.Bitmap(pad * 5 + zoom * 3, pad * 2 + zoom + 60);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.Clear(System.Drawing.Color.FromArgb(60, 60, 63));
        using var head = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);

        // dark strip (connected + disconnected zoomed, plus 16/24/32 actual)
        using (var bg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(32, 32, 34)))
            g.FillRectangle(bg, 0, 0, bmp.Width, pad + zoom + 20);
        g.DrawString("Dark taskbar — connected / disconnected / real sizes", head, System.Drawing.Brushes.White, 12, 6);
        g.DrawImage(cb, new System.Drawing.Rectangle(pad, 30, zoom, zoom));
        g.DrawImage(db, new System.Drawing.Rectangle(pad * 2 + zoom, 30, zoom, zoom));
        g.DrawImage(cb, new System.Drawing.Rectangle(pad * 3 + zoom * 2, 30, 32, 32));
        g.DrawImage(cb, new System.Drawing.Rectangle(pad * 3 + zoom * 2, 70, 24, 24));
        g.DrawImage(cb, new System.Drawing.Rectangle(pad * 3 + zoom * 2, 100, 16, 16));

        string path = Path.Combine(AppContext.BaseDirectory, "svg-icon-preview.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return 0;
    }

    /// <summary>Render all tray-icon styles at real sizes on dark + light bars → icons-preview.png.</summary>
    private static int IconsPreview()
    {
        var styles = new[]
        {
            (IconFactory.Style.Earbud,    "A  earbud"),
            (IconFactory.Style.Headphone, "B  headphone"),
            (IconFactory.Style.DotRing,   "C  dot-ring"),
            (IconFactory.Style.BudWaves,  "D  bud + waves"),
        };

        int zoom = 96, gap = 28, colW = zoom + gap, left = 30, top = 46;
        int w = left + styles.Length * colW;
        int rowH = 150;
        using var bmp = new System.Drawing.Bitmap(w, top + rowH * 2 + 20);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(System.Drawing.Color.FromArgb(60, 60, 63));

        var rows = new[]
        {
            (System.Drawing.Color.FromArgb(32, 32, 34), "On a DARK taskbar (default)"),
            (System.Drawing.Color.FromArgb(238, 238, 240), "On a LIGHT taskbar"),
        };

        using var label = new System.Drawing.Font("Segoe UI", 10);
        using var head = new System.Drawing.Font("Segoe UI", 11, System.Drawing.FontStyle.Bold);

        for (int r = 0; r < rows.Length; r++)
        {
            int rowY = top + r * rowH;
            using (var bg = new System.Drawing.SolidBrush(rows[r].Item1))
                g.FillRectangle(bg, 0, rowY - 10, w, rowH);
            bool light = r == 1;
            var textColor = light ? System.Drawing.Color.Black : System.Drawing.Color.White;
            using var tb = new System.Drawing.SolidBrush(textColor);
            g.DrawString(rows[r].Item2, head, tb, 12, rowY - 6);

            for (int i = 0; i < styles.Length; i++)
            {
                int x = left + i * colW;
                // zoomed icon (render at 32 then scale up)
                using (var ic = new System.Drawing.Bitmap(32, 32))
                {
                    using (var ig = System.Drawing.Graphics.FromImage(ic))
                    {
                        ig.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        ig.Clear(System.Drawing.Color.Transparent);
                        IconFactory.Draw(styles[i].Item1, ig, 32, light ? System.Drawing.Color.FromArgb(30, 30, 30) : System.Drawing.Color.White);
                    }
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(ic, new System.Drawing.Rectangle(x, rowY + 18, zoom, zoom));
                    // real 16px + 24px
                    g.DrawImage(ic, new System.Drawing.Rectangle(x + zoom + 4, rowY + 18, 16, 16));
                    g.DrawImage(ic, new System.Drawing.Rectangle(x + zoom + 4, rowY + 40, 24, 24));
                }
                g.DrawString(styles[i].Item2, label, r == 1 ? System.Drawing.Brushes.Black : System.Drawing.Brushes.White, x, rowY + zoom + 22);
            }
        }

        string path = Path.Combine(AppContext.BaseDirectory, "icons-preview.png");
        bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return 0;
    }

    /// <summary>Render the flyout with sample data to a PNG so the design can be verified headlessly.</summary>
    private static int Shot()
    {
        var wpf = new System.Windows.Application();
        var win = new FlyoutWindow();
        win.UpdateConnected("CMF Headphone Pro", true);
        win.UpdateBattery(new Protocol.Cell(100, false));
        win.UpdateAnc(Protocol.AncMode.Transparency);
        win.UpdateLatency(true);
        win.UpdateBass(true, 3);
        win.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        win.Show();
        win.UpdateLayout();

        int w = (int)Math.Ceiling(win.ActualWidth);
        int h = (int)Math.Ceiling(win.ActualHeight);
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(win);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        string path = Path.Combine(AppContext.BaseDirectory, "flyout-shot.png");
        using (var fs = File.Create(path)) enc.Save(fs);
        win.Close();
        return 0;
    }

    [DllImport("kernel32.dll")] private static extern bool AllocConsole();

    /// <summary>
    /// Console diagnostic: connect to a paired CMF Headphone Pro and dump battery / firmware /
    /// ANC / low-latency responses. Proves the control channel works before touching the UI.
    /// Usage:  CmfTray --probe           (auto-picks the first paired Headphone Pro)
    ///         CmfTray --probe 2         (pick candidate index 2 from the list)
    /// </summary>
    private static StreamWriter? _log;
    private static void Out(string s)
    {
        Console.WriteLine(s);
        try { _log?.WriteLine(s); _log?.Flush(); } catch { }
    }

    private static int Probe(string[] args)
    {
        AllocConsole();
        try { _log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "probe-log.txt"), append: false); }
        catch { }
        Out("=== CMF Tray — Headphone Pro probe ===\n");

        var candidates = RfcommClient.ListPairedHeadphonePros();
        if (candidates.Count == 0)
        {
            Console.WriteLine("No paired CMF Headphone Pro found. Pair it in Windows Bluetooth settings first.");
            Pause();
            return 1;
        }

        Out("Paired devices:");
        for (int i = 0; i < candidates.Count; i++)
            Out($"  [{i}] {candidates[i].Name}  (Bluetooth address omitted)" +
                (candidates[i].Connected ? "  [connected]" : "") +
                (candidates[i].Authenticated ? "" : "  (not authenticated)"));

        int idx = 0;
        var idxArg = args.FirstOrDefault(a => int.TryParse(a, out _));
        if (idxArg is not null) idx = int.Parse(idxArg);
        idx = Math.Clamp(idx, 0, candidates.Count - 1);
        var chosen = candidates[idx];
        Out($"\nUsing [{idx}] {chosen.Name}\n");

        using var client = new RfcommClient();
        var deviceInfoReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Log += m => Out($"  · {m}");
        client.FrameReceived += frame =>
        {
            ushort cmd = Protocol.GetCommand(frame);
            // Device-info includes a serial number and Bluetooth address; never log that payload.
            Out(cmd == 0x4006
                ? "  <- device-info response (identity fields omitted)  (cmd 0x4006)"
                : $"  <- {Protocol.Hex(frame)}  (cmd 0x{cmd:X4})");
            if (cmd == 0x4006) deviceInfoReceived.TrySetResult(true);
            HandleProbeFrame(cmd, frame);
        };

        try
        {
            client.Connect(chosen.Address, chosen.Name);
        }
        catch (Exception ex)
        {
            Out($"\nCONNECT FAILED: {ex.Message}");
            Out("If this says the service is unreachable, make sure the headphones are connected " +
                "to this PC and Nothing X or another controller has released the control channel.");
            Pause();
            return 2;
        }

        Out("\nPriming the control session with device-info…");
        Send(client, "device info", Protocol.ReadDeviceInfo());
        if (!deviceInfoReceived.Task.Wait(TimeSpan.FromMilliseconds(1500)))
            Out("  No device-info response within 1.5s; continuing with state queries.");

        Out("\nSending queries…");
        Send(client, "battery",  Protocol.ReadBattery());
        Thread.Sleep(200);
        Send(client, "anc read", Protocol.ReadAnc());
        Thread.Sleep(200);
        Send(client, "latency", Protocol.ReadLatency());

        Out("\nListening for responses for 5s…");
        Thread.Sleep(5000);
        Out("\nProbe complete. See probe-log.txt for this transcript.");
        Pause();
        return 0;
    }

    private static volatile byte[]? _lastAncRead;

    /// <summary>
    /// Empirically maps every ANC mode: set it, read it back, log the raw response so we can
    /// confirm the set-payload and the read-parser against real hardware. Restores original mode.
    /// </summary>
    private static int AncMap(string[] args)
    {
        AllocConsole();
        try { _log = new StreamWriter(Path.Combine(AppContext.BaseDirectory, "anc-map-log.txt"), append: false); }
        catch { }
        Out("=== CMF Tray — ANC mapping ===\n");

        var candidates = RfcommClient.ListPairedHeadphonePros();
        if (candidates.Count == 0) { Out("No paired CMF Headphone Pro found."); Pause(); return 1; }
        int idx = 0;
        var idxArg = args.FirstOrDefault(a => int.TryParse(a, out _));
        if (idxArg is not null) idx = Math.Clamp(int.Parse(idxArg), 0, candidates.Count - 1);
        var chosen = candidates[idx];
        Out($"Using [{idx}] {chosen.Name}\n");

        using var client = new RfcommClient();
        var deviceInfoReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Log += m => Out($"  · {m}");
        client.FrameReceived += frame =>
        {
            ushort cmd = Protocol.GetCommand(frame);
            if (cmd == 0x4006) deviceInfoReceived.TrySetResult(true);
            if (cmd is 0x401E or 0xE003 or 0x401F) _lastAncRead = frame;
        };

        try { client.Connect(chosen.Address, chosen.Name); }
        catch (Exception ex) { Out($"CONNECT FAILED: {ex.Message}"); Pause(); return 2; }

        client.Send(Protocol.ReadDeviceInfo());
        if (!deviceInfoReceived.Task.Wait(TimeSpan.FromMilliseconds(1500)))
            Out("No device-info response within 1.5s; continuing.");

        // Capture original.
        client.Send(Protocol.ReadAnc());
        Thread.Sleep(500);
        var original = _lastAncRead;
        Out($"Original ANC read: {(original is null ? "(none)" : Protocol.Hex(original))}\n");

        var modes = new (string Name, Protocol.AncMode Mode)[]
        {
            ("Off",          Protocol.AncMode.Off),
            ("Transparency", Protocol.AncMode.Transparency),
            ("ANC High",     Protocol.AncMode.AncHigh),
            ("ANC Mid",      Protocol.AncMode.AncMid),
            ("ANC Low",      Protocol.AncMode.AncLow),
            ("ANC Adaptive", Protocol.AncMode.AncAdaptive),
        };

        foreach (var (name, mode) in modes)
        {
            _lastAncRead = null;
            var setFrame = Protocol.SetAnc(mode);
            client.Send(setFrame);
            Thread.Sleep(350);
            client.Send(Protocol.ReadAnc());
            Thread.Sleep(400);
            string read = _lastAncRead is null ? "(no read)" : Protocol.Hex(_lastAncRead);
            Out($"  SET {name,-13} (0x{(byte)mode:X2})  set={Protocol.Hex(setFrame)}");
            Out($"      read-back  {read}");
        }

        // Restore the mode from the kind-1 field; kind-2 is ANC strength on Headphone Pro.
        var originalMode = original is null ? null : Protocol.ParseAnc(original);
        if (originalMode is not null)
        {
            Out($"\nRestoring original mode 0x{(byte)originalMode.Value:X2}…");
            client.Send(Protocol.SetAnc(originalMode.Value));
            Thread.Sleep(300);
        }

        Out("\nDone. See anc-map-log.txt.");
        Pause();
        return 0;
    }

    private static void Send(RfcommClient c, string label, byte[] frame)
    {
        Out($"  -> {label,-9} {Protocol.Hex(frame)}");
        c.Send(frame);
    }

    private static void HandleProbeFrame(ushort cmd, byte[] frame)
    {
        // Battery push/response commands seen in the wild.
        if (cmd is 0x4007 or 0xE001)
        {
            var b = Protocol.ParseBattery(frame);
            Out($"     BATTERY  {Fmt(b.BatteryPct, b.BatteryCharging)}");
        }
        else if (cmd == 0x4041)
        {
            var latency = Protocol.ParseLatency(frame);
            string state = latency is null ? "(unparsed)" : latency.Value ? "ON" : "OFF";
            Out($"     LOW LATENCY {state}");
        }
        else if (cmd == 0x4006)
        {
            var version = Protocol.ParseDeviceInfo(frame);
            Out($"     DEVICE INFO firmware={version.Firmware} software={version.Software}; identity fields withheld.");
        }
    }

    private static string Fmt(int? pct, bool charging) =>
        pct is null ? "—" : $"{pct}%{(charging ? "⚡" : "")}";

    private static void Pause()
    {
        Console.WriteLine("\nPress any key to close…");
        try { Console.ReadKey(); } catch { Thread.Sleep(4000); }
    }
}
