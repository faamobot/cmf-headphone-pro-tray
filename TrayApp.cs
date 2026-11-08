using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace CmfTray;

/// <summary>
/// System-tray front end. Owns the Bluetooth client and the WPF flyout window; a WinForms
/// NotifyIcon provides the tray icon. State reacts to pushed frames in real time, with a slow
/// poll as a safety net.
/// </summary>
public sealed class TrayApp : IDisposable
{
    private readonly System.Windows.Application _wpf;
    private readonly NotifyIcon _tray;
    private readonly FlyoutWindow _flyout = new();
    private readonly RfcommClient _client = new();
    private readonly DispatcherTimer _battTimer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _stateTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer _trimTimer = new() { Interval = TimeSpan.FromMinutes(2) };

    private Protocol.AncMode? _ancMode;
    private bool _latencyEnabled;
    private bool _bassEnabled;
    private int _bassLevel;
    private RfcommClient.Candidate? _device;
    private volatile bool _connecting;
    private int _connectionGeneration;
    private int _initialStateReady;

    public TrayApp(System.Windows.Application wpf)
    {
        _wpf = wpf;

        _tray = new NotifyIcon
        {
            Icon = MakeIcon(false),
            Text = "CMF Headphone Pro — disconnected",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Opening += (_, _) => RebuildMenu();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _flyout.ToggleAtTray(); };

        _flyout.SetAnc = mode => { TrySend(Protocol.SetAnc(mode)); DelayedRead(); };
        _flyout.SetLatency = SetLatencyMode;
        _flyout.SetBassEnabled = on => { _bassEnabled = on; TrySend(Protocol.SetEnhancedBass(on, _bassLevel)); };
        _flyout.SetBassLevel = lvl => { _bassLevel = lvl; TrySend(Protocol.SetEnhancedBass(_bassEnabled, lvl)); };
        _flyout.OnShown = RefreshState;

        _client.FrameReceived += OnFrame;
        _client.Disconnected += () => Post(OnDisconnected);
        _client.Log += _ => { };

        // Battery refresh + a liveness poll (a failed send flips us to disconnected).
        _battTimer.Tick += (_, _) => { if (CanQueryDevice) TrySend(Protocol.ReadBattery()); };
        _stateTimer.Tick += (_, _) =>
        {
            if (CanQueryDevice)
            {
                TrySend(Protocol.ReadAnc());
                TrySend(Protocol.ReadLatency());
            }
        };
        // Cheap watcher: only opens the control channel when the headphones are on THIS PC.
        _reconnectTimer.Tick += (_, _) => { if (!_client.IsConnected) BeginAutoConnect(); };
        // Keep the idle footprint small.
        _trimTimer.Tick += (_, _) => TrimMemory();
        _battTimer.Start();
        _stateTimer.Start();
        _reconnectTimer.Start();
        _trimTimer.Start();

        _flyout.UpdateConnected("CMF Headphone Pro", false);
        // First connection attempt is deferred to the reconnect timer so startup stays light.
        // Trim the working set a few seconds after launch, once WPF/Bluetooth init settles.
        var warmup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        warmup.Tick += (_, _) => { warmup.Stop(); TrimMemory(); };
        warmup.Start();
    }

    private void RefreshState()
    {
        if (!CanQueryDevice) return;
        TrySend(Protocol.ReadBattery());
        TrySend(Protocol.ReadAnc());
        TrySend(Protocol.ReadLatency());
        TrySend(Protocol.ReadEnhancedBass());
    }

    // ---------------------------------------------------------------- tray menu (right-click)

    private void RebuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();

        m.Items.Add(new ToolStripMenuItem(_client.IsConnected ? $"● {_device?.Name ?? "Connected"}" : "○ Disconnected") { Enabled = false });
        m.Items.Add(new ToolStripSeparator());

        bool ancOn = _ancMode is Protocol.AncMode.AncHigh or Protocol.AncMode.AncMid or
            Protocol.AncMode.AncLow or Protocol.AncMode.AncAdaptive;
        var anc = new ToolStripMenuItem("Noise Control") { Enabled = _client.IsConnected };
        anc.DropDownItems.Add(AncItem("Noise Cancellation", Protocol.AncMode.AncHigh, ancOn));
        anc.DropDownItems.Add(AncItem("Transparency", Protocol.AncMode.Transparency, _ancMode == Protocol.AncMode.Transparency));
        anc.DropDownItems.Add(AncItem("Off", Protocol.AncMode.Off, _ancMode == Protocol.AncMode.Off));
        m.Items.Add(anc);

        var lat = new ToolStripMenuItem("Low Latency Mode (Game Mode)") { Enabled = _client.IsConnected };
        lat.DropDownItems.Add(new ToolStripMenuItem("On", null, (_, _) => SetLatencyMode(true)) { Checked = _latencyEnabled });
        lat.DropDownItems.Add(new ToolStripMenuItem("Off", null, (_, _) => SetLatencyMode(false)) { Checked = !_latencyEnabled });
        m.Items.Add(lat);

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Open panel", null, (_, _) => _flyout.ShowAtTray()));
        m.Items.Add(new ToolStripMenuItem(_client.IsConnected ? "Reconnect" : "Connect", null, (_, _) => { _client.Disconnect(); BeginAutoConnect(); }));
        m.Items.Add(new ToolStripMenuItem("Choose device…", null, (_, _) => ChooseDevice()));
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) => SetAutoStart(!IsAutoStart())) { Checked = IsAutoStart() });
        m.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => Quit()));
    }

    // ---------------------------------------------------------------- run at login

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "CmfTray";

    private static bool IsAutoStart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(RunValue) is not null;
    }

    private static void SetAutoStart(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else if (k.GetValue(RunValue) is not null) k.DeleteValue(RunValue);
    }

    private ToolStripMenuItem AncItem(string label, Protocol.AncMode mode, bool isChecked) =>
        new(label, null, (_, _) => { TrySend(Protocol.SetAnc(mode)); DelayedRead(); }) { Checked = isChecked };

    private void DelayedRead()
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { t.Stop(); if (_client.IsConnected) TrySend(Protocol.ReadAnc()); };
        t.Start();
    }

    private void SetLatencyMode(bool enabled)
    {
        _latencyEnabled = enabled;
        _flyout.UpdateLatency(enabled);
        TrySend(Protocol.SetLatency(enabled));

        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        t.Tick += (_, _) => { t.Stop(); if (_client.IsConnected) TrySend(Protocol.ReadLatency()); };
        t.Start();
    }

    // ---------------------------------------------------------------- connection

    /// <summary>
    /// Cheaply check whether the Headphone Pro is actually connected to this PC, and only then open
    /// the control channel. When they're not present we do nothing — no blocking connect attempts,
    /// no background churn. This is what keeps the idle footprint tiny.
    /// </summary>
    private void BeginAutoConnect()
    {
        if (_connecting || _client.IsConnected) return;
        _connecting = true;
        Task.Run(() =>
        {
            try
            {
                var list = RfcommClient.ListPairedHeadphonePros();
                var target = list.Count > 0 ? list[0] : null;

                // Not paired, or not currently connected to this PC → stay idle and quiet.
                if (target is null || !target.Connected)
                {
                    Post(() => _flyout.UpdateConnected(target?.Name ?? _device?.Name ?? "CMF Headphone Pro", false));
                    return;
                }

                Post(() => _flyout.SetStatus("Connecting…"));
                ConnectCore(target);
            }
            catch
            {
                Post(() => _flyout.UpdateConnected(_device?.Name ?? "CMF Headphone Pro", false));
            }
            finally { _connecting = false; }
        });
    }

    private void ChooseDevice()
    {
        var list = RfcommClient.ListPairedHeadphonePros();
        if (list.Count == 0) { Notify("No paired CMF Headphone Pro found in Windows Bluetooth settings."); return; }
        using var dlg = new DevicePicker(list);
        if (dlg.ShowDialog() == DialogResult.OK && dlg.Selected is { } sel && !_connecting)
        {
            _connecting = true;
            Task.Run(() => { try { ConnectCore(sel); } finally { _connecting = false; } });
        }
    }

    /// <summary>Actual connect on a background thread; marshals UI updates back to the dispatcher.</summary>
    private void ConnectCore(RfcommClient.Candidate c)
    {
        try
        {
            _client.Connect(c.Address, c.Name);
            _device = c;
            Post(() =>
            {
                _tray.Icon = MakeIcon(true);
                _tray.Text = $"CMF Tray — {c.Name}";
                _flyout.UpdateConnected(c.Name, true);
            });
            BeginDeviceInitialization();
        }
        catch
        {
            // Headphones are elsewhere (e.g. on another device) or unreachable — the timer retries.
            _client.Disconnect();
            Post(() => _flyout.UpdateConnected(_device?.Name ?? "CMF Headphone Pro", false));
        }
    }

    private void OnDisconnected()
    {
        Interlocked.Increment(ref _connectionGeneration);
        Interlocked.Exchange(ref _initialStateReady, 0);
        _tray.Icon = MakeIcon(false);
        _tray.Text = "CMF Headphone Pro — disconnected";
        _ancMode = null;
        _latencyEnabled = false;
        _flyout.UpdateConnected(_device?.Name ?? "CMF Headphone Pro", false);
        _flyout.UpdateBattery(default);
        _flyout.UpdateAnc(null);
        _flyout.UpdateLatency(false);
        TrimMemory(); // going idle — release memory back
    }

    /// <summary>Send a frame; on any failure, tear down so the reconnect loop can recover.</summary>
    private void TrySend(byte[] frame)
    {
        try { _client.Send(frame); }
        catch { OnDrop(); }
    }

    private void OnDrop()
    {
        _client.Disconnect();
        Post(OnDisconnected);
    }

    private void OnFrame(byte[] frame)
    {
        ushort cmd = Protocol.GetCommand(frame);
        switch (cmd)
        {
            case 0x4007 or 0xE001:
            {
                var b = Protocol.ParseBattery(frame);
                Post(() =>
                {
                    string battery = b.BatteryPct is null ? "battery —" : $"{b.BatteryPct}%";
                    _tray.Text = $"CMF Headphone Pro — {battery}";
                    _flyout.UpdateBattery(b);
                });
                break;
            }
            case 0x401E or 0xE003:
            {
                var mode = Protocol.ParseAnc(frame);
                if (mode is not null) Post(() => { _ancMode = mode; _flyout.UpdateAnc(mode); });
                break;
            }
            case 0x4006:
            {
                // A fresh NT Link session may ignore normal reads until this info query completes.
                if (Interlocked.Exchange(ref _initialStateReady, 1) == 0)
                    Post(RefreshState);
                break;
            }
            case 0x4041:
            {
                var enabled = Protocol.ParseLatency(frame);
                if (enabled is not null)
                    Post(() => { _latencyEnabled = enabled.Value; _flyout.UpdateLatency(enabled.Value); });
                break;
            }
            case 0x404E:
            {
                var (enabled, level) = Protocol.ParseEnhancedBass(frame);
                Post(() => { _bassEnabled = enabled; _bassLevel = level; _flyout.UpdateBass(enabled, level); });
                break;
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    private void Post(Action a) => _flyout.Dispatcher.BeginInvoke(a);

    private bool CanQueryDevice => _client.IsConnected && Volatile.Read(ref _initialStateReady) != 0;

    private void BeginDeviceInitialization()
    {
        int generation = Interlocked.Increment(ref _connectionGeneration);
        Interlocked.Exchange(ref _initialStateReady, 0);
        TrySend(Protocol.ReadDeviceInfo());
        _ = ContinueInitializationAfterTimeoutAsync(generation);
    }

    private async Task ContinueInitializationAfterTimeoutAsync(int generation)
    {
        // Older Nothing/CMF models may not answer device-info; do not block normal reads forever.
        await Task.Delay(TimeSpan.FromMilliseconds(1500));
        if (generation != Volatile.Read(ref _connectionGeneration) || !_client.IsConnected) return;
        if (Interlocked.Exchange(ref _initialStateReady, 1) == 0) Post(RefreshState);
    }

    private void Notify(string msg) => _tray.ShowBalloonTip(4000, "CMF Tray", msg, ToolTipIcon.Info);

    // Trim the working set so an idle tray app doesn't sit on ~100 MB of RAM.
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr hProcess);
    private static void TrimMemory() { try { EmptyWorkingSet(GetCurrentProcess()); } catch { } }

    /// <summary>Tray icon: over-ear headphone silhouette (white when connected, grey when not).</summary>
    private static Icon MakeIcon(bool connected) => IconFactory.Make(IconFactory.Style.Headphone, connected);

    private void Quit()
    {
        _tray.Visible = false;
        Dispose();
        _wpf.Shutdown();
    }

    public void Dispose()
    {
        _battTimer.Stop();
        _stateTimer.Stop();
        _reconnectTimer.Stop();
        _trimTimer.Stop();
        _client.Dispose();
        try { _flyout.Close(); } catch { }
        _tray.Dispose();
    }
}
