using System.IO;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace CmfTray;

/// <summary>
/// Opens the CMF Headphone Pro's NT Link RFCOMM control channel and pumps framed packets in/out.
/// One physical connection; reads run on a background thread and surface as <see cref="FrameReceived"/>.
/// </summary>
public sealed class RfcommClient : IDisposable
{
    private BluetoothClient? _client;
    private Stream? _stream;
    private Thread? _reader;
    private volatile bool _running;

    public bool IsConnected => _stream is not null && _running;
    public string? DeviceName { get; private set; }

    /// <summary>Raised on the reader thread for each complete NT Link frame (including pushed events).</summary>
    public event Action<byte[]>? FrameReceived;
    public event Action<string>? Log;
    public event Action? Disconnected;

    public sealed record Candidate(string Name, BluetoothAddress Address, bool Authenticated, bool Connected);

    /// <summary>Find the paired CMF Headphone Pro devices this fork is designed to control.</summary>
    public static IReadOnlyList<Candidate> ListPairedHeadphonePros()
    {
        using var cli = new BluetoothClient();
        return cli.PairedDevices
            .Select(d => new Candidate(d.DeviceName ?? "(unknown)", d.DeviceAddress, d.Authenticated, d.Connected))
            .Where(c => c.Name.Contains("Headphone Pro", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void Connect(BluetoothAddress address, string name)
    {
        Disconnect();
        _client = new BluetoothClient();
        Log?.Invoke($"Connecting to {name} on NT Link RFCOMM {Protocol.SppServiceUuid}…");
        _client.Connect(address, Protocol.SppServiceUuid);
        _stream = _client.GetStream();
        DeviceName = name;
        _running = true;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "SppReader" };
        _reader.Start();
        Log?.Invoke("Connected.");
    }

    public void Send(byte[] frame)
    {
        var s = _stream ?? throw new InvalidOperationException("Not connected.");
        lock (s)
        {
            s.Write(frame, 0, frame.Length);
            s.Flush();
        }
    }

    private void ReadLoop()
    {
        var buf = new byte[1024];
        var acc = new List<byte>();
        try
        {
            while (_running && _stream is not null)
            {
                int n = _stream.Read(buf, 0, buf.Length);
                if (n <= 0) break;
                acc.AddRange(buf.AsSpan(0, n).ToArray());
                DrainFrames(acc);
            }
        }
        catch (Exception ex) when (_running)
        {
            Log?.Invoke($"Read error: {ex.Message}");
        }
        finally
        {
            _running = false;
            Disconnected?.Invoke();
        }
    }

    /// <summary>
    /// Split the stream using its payload length and control-word CRC flag. Replies use 0x55 0x60
    /// with a CRC; Headphone Pro state events can use 0x55 0x00 with no CRC.
    /// </summary>
    private void DrainFrames(List<byte> acc)
    {
        while (true)
        {
            // Find the next frame start. State events do not use the 0x60 control byte.
            int start = -1;
            for (int i = 0; i + 2 < acc.Count; i++)
                if (acc[i] == 0x55 && (acc[i + 1] is 0x00 or 0x20 or 0x60)) { start = i; break; }
            if (start < 0)
            {
                if (acc.Count > 512) acc.Clear(); // resync guard
                return;
            }
            if (start > 0) acc.RemoveRange(0, start);

            if (acc.Count < 8) return;            // need header
            int payloadLen = acc[5];
            bool hasCrc = (acc[1] & 0x20) != 0;
            int total = 8 + payloadLen + (hasCrc ? 2 : 0);
            if (acc.Count < total) return;        // wait for the rest

            var frame = acc.GetRange(0, total).ToArray();
            acc.RemoveRange(0, total);
            try { FrameReceived?.Invoke(frame); }
            catch (Exception ex) { Log?.Invoke($"Frame handler error: {ex.Message}"); }
        }
    }

    public void Disconnect()
    {
        _running = false;
        try { _stream?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        _stream = null;
        _client = null;
    }

    public void Dispose() => Disconnect();
}
