using System.Text;

namespace CmfTray;

/// <summary>
/// Nothing / CMF "wire" protocol over the Bluetooth SPP control channel.
///
/// Frame layout (little-endian command, CRC appended little-endian):
///   [0x55, 0x60, 0x01, cmdLo, cmdHi, payloadLen, 0x00, seq, ...payload, crcLo, crcHi]
///
/// CRC is CRC-16 (reflected, init 0xFFFF, poly 0xA001) over every byte before the CRC.
/// Reverse-engineered by the community; CMF Headphone Pro uses the same NT Link frames on
/// RFCOMM channel 28 and requires a device-info query before normal state queries.
/// </summary>
public static class Protocol
{
    // The NT Link RFCOMM service exposed for control (separate from A2DP/HFP audio).
    public static readonly Guid SppServiceUuid = Guid.Parse("aeac4a03-dff5-498f-843a-34487cf133eb");

    // --- Command IDs (16-bit) ---
    public const ushort CmdSetAnc          = 0xF00F; // 61455  payload [0x01, mode, 0x00]
    public const ushort CmdReadAnc         = 0xC01E; // 49182
    public const ushort CmdSetEq           = 0xF010; // 61456  payload [level, 0x00]   (non-CMF)
    public const ushort CmdReadEq          = 0xC01F; // 49183
    public const ushort CmdSetListenMode   = 0xF01D; // 61469  payload [level, 0x00]   (CMF: B172/B168)
    public const ushort CmdReadListenMode  = 0xC050; // 49232
    public const ushort CmdReadBattery     = 0xC007; // 49159
    public const ushort CmdReadDeviceInfo  = 0xC006; // 49158; primes a fresh NT Link session
    public const ushort CmdReadFirmware    = 0xC042; // 49218
    public const ushort CmdSetLatency      = 0xF040; // 61504  payload [0x01]=on, [0x02]=off
    public const ushort CmdReadLatency     = 0xC041; // 49217; answer command is 0x4041
    public const ushort CmdSetInEar        = 0xF004; // 61444
    public const ushort CmdSetEnhancedBass = 0xF051; // 61521  payload [enabled, level*2]   (CMF)
    public const ushort CmdReadEnhancedBass= 0xC04E; // 49230

    /// <summary>
    /// ANC modes as the device encodes them in the kind-1 value field. CMF Headphone Pro supports
    /// Adaptive (0x04); other Nothing/CMF models may expose a different set of supported modes.
    /// </summary>
    public enum AncMode : byte
    {
        Off          = 0x05,
        Transparency = 0x07,
        AncHigh      = 0x01,
        AncMid       = 0x02,
        AncLow       = 0x03,
        AncAdaptive  = 0x04, // Nothing Ear / Pro only
    }

    /// <summary>
    /// Current ANC mode from a read response (0x401E) or event (0xE003). The payload is one or
    /// more 3-byte kind/value/reserved groups; kind 1 is the mode and kind 2 is ANC strength.
    /// Headphone Pro sends both groups, so reading the old fixed byte 12 returns strength instead.
    /// </summary>
    public static AncMode? ParseAnc(byte[] d)
    {
        if (d.Length < 11) return null;
        int trailerLength = (d[1] & 0x20) != 0 ? 2 : 0;
        int end = Math.Min(d.Length - trailerLength, 8 + d[5]);
        for (int i = 8; i + 1 < end; i += 3)
        {
            if (d[i] != 0x01) continue;
            var mode = (AncMode)d[i + 1];
            return Enum.IsDefined(typeof(AncMode), mode) ? mode : null;
        }
        return null;
    }

    private static byte _seq = 0;
    private static byte NextSeq()
    {
        _seq++;
        if (_seq >= 250) _seq = 1;
        return _seq;
    }

    /// <summary>Builds a complete framed packet (header + payload + CRC) ready to write to the socket.</summary>
    public static byte[] Build(ushort command, params byte[] payload)
    {
        payload ??= Array.Empty<byte>();
        var frame = new List<byte>(8 + payload.Length + 2)
        {
            0x55, 0x60, 0x01,
            (byte)(command & 0xFF),        // cmdLo
            (byte)((command >> 8) & 0xFF), // cmdHi
            (byte)payload.Length,          // payload length
            0x00,
            NextSeq(),
        };
        frame.AddRange(payload);

        ushort crc = Crc16(frame);
        frame.Add((byte)(crc & 0xFF));
        frame.Add((byte)((crc >> 8) & 0xFF));
        return frame.ToArray();
    }

    // --- Convenience builders ---
    public static byte[] SetAnc(AncMode mode)   => Build(CmdSetAnc, 0x01, (byte)mode, 0x00);
    public static byte[] ReadBattery()          => Build(CmdReadBattery);
    public static byte[] ReadDeviceInfo()       => Build(CmdReadDeviceInfo);
    public static byte[] ReadAnc()              => Build(CmdReadAnc);
    public static byte[] ReadFirmware()         => Build(CmdReadFirmware);
    public static byte[] SetEq(byte preset)     => Build(CmdSetEq, preset, 0x00);
    public static byte[] SetListenMode(byte m)  => Build(CmdSetListenMode, m, 0x00);
    public static byte[] SetLatency(bool on)    => Build(CmdSetLatency, (byte)(on ? 0x01 : 0x02));
    public static byte[] ReadLatency()          => Build(CmdReadLatency);
    public static bool? ParseLatency(byte[] d) =>
        d.Length >= 9 && d[5] >= 1 ? d[8] == 0x01 : null;

    public readonly record struct DeviceVersion(string Firmware, string Software);

    /// <summary>Reads only version fields from device-info; serial and Bluetooth address are ignored.</summary>
    public static DeviceVersion ParseDeviceInfo(byte[] d)
    {
        if (d.Length < 9) return new DeviceVersion("", "");
        int end = Math.Min(d.Length - 2, 8 + d[5]);
        if (end <= 8) return new DeviceVersion("", "");

        string text = Encoding.ASCII.GetString(d, 8, end - 8).TrimStart('\0', '\u0004');
        string firmware = "", software = "";
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Trim('\r').Split(',');
            if (fields.Length < 3 || fields[0] != "6") continue;
            if (fields[1] == "1") firmware = fields[2];
            else if (fields[1] == "2") software = fields[2];
        }
        return new DeviceVersion(firmware, software);
    }
    public static byte[] ReadEnhancedBass()     => Build(CmdReadEnhancedBass);

    /// <summary>Ultra bass: payload [enabled, level*2]. Supported on CMF (B172/B168) per ear-web.</summary>
    public static byte[] SetEnhancedBass(bool enabled, int level) =>
        Build(CmdSetEnhancedBass, (byte)(enabled ? 1 : 0), (byte)(Math.Clamp(level, 0, 6) * 2));

    /// <summary>Enhanced-bass read (cmd 0x404E): enabled at byte[8], level*2 at byte[9].</summary>
    public static (bool enabled, int level) ParseEnhancedBass(byte[] d) =>
        d.Length >= 10 ? (d[8] != 0, d[9] / 2) : (false, 0);

    /// <summary>CRC-16 (reflected, init 0xFFFF, poly 0xA001).</summary>
    public static ushort Crc16(IReadOnlyList<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int j = 0; j < 8; j++)
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
        }
        return crc;
    }

    // ---------------------------------------------------------------------
    //  Response parsing
    // ---------------------------------------------------------------------

    public static ushort GetCommand(byte[] data) =>
        data.Length >= 5 ? (ushort)(data[3] | (data[4] << 8)) : (ushort)0;

    public static bool IsValidFrame(byte[] data)
    {
        if (data.Length < 8 || data[0] != 0x55) return false;
        int trailerLength = (data[1] & 0x20) != 0 ? 2 : 0;
        return data.Length >= 8 + data[5] + trailerLength;
    }

    public readonly record struct Cell(int? BatteryPct, bool BatteryCharging);

    /// <summary>
    /// Battery response: payload[0] (=index 8) is the count of reported devices; then pairs of
    /// [deviceId, value] where value&0x7F is the percentage and value&0x80 is the charging flag.
    /// CMF Headphone Pro reports its single battery as component 0x06.
    /// </summary>
    public static Cell ParseBattery(byte[] d)
    {
        if (d.Length < 9) return new Cell(null, false);

        int count = d[8];
        for (int i = 0; i < count; i++)
        {
            int idIdx = 9 + i * 2;
            int valIdx = 10 + i * 2;
            if (valIdx >= d.Length) break;
            int pct = d[valIdx] & 0x7F;
            bool charging = (d[valIdx] & 0x80) != 0;
            if (d[idIdx] == 0x06) return new Cell(pct, charging);
        }
        return new Cell(null, false);
    }

    /// <summary>Firmware response: length at index 5, ASCII string starting at index 8.</summary>
    public static string ParseFirmware(byte[] d)
    {
        if (d.Length < 9) return "";
        int len = d[5];
        int end = Math.Min(8 + len, d.Length);
        var sb = new StringBuilder();
        for (int i = 8; i < end; i++)
            if (d[i] >= 0x20 && d[i] < 0x7F) sb.Append((char)d[i]);
        return sb.ToString();
    }

    public static string Hex(byte[] d) => Convert.ToHexString(d);
}
