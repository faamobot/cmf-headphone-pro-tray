using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace CmfTray;

public partial class FlyoutWindow : Window
{
    private static readonly Brush White = Brushes.White;
    private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush Clear = Brushes.Transparent;

    private const int BatteryFillWidth = 12; // battery icon interior width (design units)
    private const int SliderSpan = 200;  // usable slider width for the bass fill
    private const int MaxBass = 5;

    public Action<Protocol.AncMode>? SetAnc;
    public Action<bool>? SetLatency;
    public Action<bool>? SetBassEnabled;
    public Action<int>? SetBassLevel;
    public Action? OnShown;

    private Protocol.AncMode? _anc;
    private bool _latencyOn;
    private bool _bassOn;
    private int _bassLevel;
    private DateTime _lastHide = DateTime.MinValue;

    public FlyoutWindow()
    {
        InitializeComponent();
        Dark.Freeze();
        RefreshAnc();
        RefreshLatency();
        RefreshBass();
    }

    // ---------- Windows acrylic blur-behind (SetWindowCompositionAttribute) ----------
    private enum AccentState { Disabled = 0, EnableBlurBehind = 3, EnableAcrylicBlurBehind = 4 }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy { public int AccentState; public int AccentFlags; public uint GradientColor; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }

    [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Rounded corners + dark mode (Win11).
        int round = 2, dark = 1;
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));

        // Acrylic blur behind. GradientColor is 0xAABBGGRR — low alpha so the blur shows through.
        var accent = new AccentPolicy
        {
            AccentState = (int)AccentState.EnableAcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = 0x33171412,
            AnimationId = 0,
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    // ---------- click handlers ----------
    private void SegNoise_Click(object sender, MouseButtonEventArgs e) => SelectAnc(Protocol.AncMode.AncHigh);
    private void SegTrans_Click(object sender, MouseButtonEventArgs e) => SelectAnc(Protocol.AncMode.Transparency);
    private void SegOff_Click(object sender, MouseButtonEventArgs e) => SelectAnc(Protocol.AncMode.Off);

    private void SelectAnc(Protocol.AncMode mode)
    {
        // Update immediately; a later device read/event remains authoritative if the command fails.
        _anc = mode;
        RefreshAnc();
        SetAnc?.Invoke(mode);
    }

    private void LatencyToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _latencyOn = !_latencyOn;
        RefreshLatency();
        SetLatency?.Invoke(_latencyOn);
    }

    private void BassToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _bassOn = !_bassOn;
        RefreshBass();
        SetBassEnabled?.Invoke(_bassOn);
    }

    private void BassSlider_Click(object sender, MouseButtonEventArgs e)
    {
        double x = e.GetPosition(BassSlider).X;
        double frac = Math.Clamp(x / Math.Max(1, BassSlider.ActualWidth), 0, 1);
        _bassLevel = Math.Clamp((int)Math.Round(frac * MaxBass), 0, MaxBass);
        RefreshBass();
        SetBassLevel?.Invoke(_bassLevel);
    }

    // ---------- state from device ----------
    public void UpdateBattery(Protocol.Cell b)
    {
        BatteryPct.Text = b.BatteryPct is null ? "—" : $"{b.BatteryPct}%";
        BatFill.Width = b.BatteryPct is null
            ? 0
            : Math.Clamp(b.BatteryPct.Value / 100.0 * BatteryFillWidth, 0, BatteryFillWidth);
        BatteryIcon.ToolTip = b.BatteryCharging ? "Headphone battery — charging" : "Headphone battery";
    }

    public void UpdateConnected(string name, bool connected)
    {
        TxtName.Text = name;
        TxtStatus.Text = connected ? "Connected" : "Disconnected";
    }

    public void SetStatus(string text) => TxtStatus.Text = text;

    public void UpdateAnc(Protocol.AncMode? mode)
    {
        _anc = mode;
        RefreshAnc();
    }

    public void UpdateLatency(bool enabled)
    {
        _latencyOn = enabled;
        RefreshLatency();
    }

    public void UpdateBass(bool enabled, int level)
    {
        _bassOn = enabled;
        _bassLevel = Math.Clamp(level, 0, MaxBass);
        RefreshBass();
    }

    // ---------- visuals ----------
    private void RefreshAnc()
    {
        bool nc = _anc is Protocol.AncMode.AncHigh or Protocol.AncMode.AncMid or
            Protocol.AncMode.AncLow or Protocol.AncMode.AncAdaptive;
        bool tr = _anc == Protocol.AncMode.Transparency;
        bool off = _anc == Protocol.AncMode.Off;

        SegNoise.Background = nc ? White : Clear;
        NoiseRing.Stroke = nc ? Dark : White;
        NoiseDot.Fill = nc ? Dark : White;

        SegTrans.Background = tr ? White : Clear;
        TransRing.Stroke = tr ? Dark : White;
        TransDot.Fill = tr ? Dark : White;

        SegOff.Background = off ? White : Clear;
        OffRing.Stroke = off ? Dark : White;
        OffLine.Stroke = off ? Dark : White;
    }

    private void RefreshBass()
    {
        BassToggleBg.Background = _bassOn
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))
            : (Brush)FindResource("InsetBg");
        BassKnob.Fill = _bassOn ? Dark : White;
        BassKnob.HorizontalAlignment = _bassOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        BassFill.Width = _bassLevel <= 0 ? 0 : _bassLevel / (double)MaxBass * SliderSpan;
    }

    private void RefreshLatency()
    {
        LatencyToggleBg.Background = _latencyOn ? White : (Brush)FindResource("InsetBg");
        LatencyKnob.Fill = _latencyOn ? Dark : White;
        LatencyKnob.HorizontalAlignment = _latencyOn ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }

    // ---------- show / hide ----------
    public void ShowAtTray()
    {
        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth - 12;
        Top = wa.Bottom - ActualHeight - 12;
        Activate();
        OnShown?.Invoke();
    }

    public void ToggleAtTray()
    {
        if (IsVisible) { Hide(); _lastHide = DateTime.UtcNow; return; }
        if ((DateTime.UtcNow - _lastHide).TotalMilliseconds < 300) return;
        ShowAtTray();
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        Hide();
        _lastHide = DateTime.UtcNow;
    }
}
