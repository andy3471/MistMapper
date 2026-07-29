using System.Buffers.Binary;

namespace MistMapper.Host.DualSense;

/// <summary>CRC-32 (IEEE / zlib) used for DualSense Bluetooth HID reports (SDL-compatible).</summary>
public static class DualSenseCrc32
{
    static readonly uint[] Table = CreateTable();

    static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            table[i] = crc;
        }
        return table;
    }

    /// <summary>Verify BT input report CRC (seed byte <c>0xA1</c> prepended to the HID report).</summary>
    public static bool VerifyBluetoothInput(ReadOnlySpan<byte> report)
    {
        if (report.Length < 8) return false;
        uint crc = HashWithSeed(0xA1, report[..^4]);
        uint packet = BinaryPrimitives.ReadUInt32LittleEndian(report[^4..]);
        return crc == packet;
    }

    /// <summary>Append CRC for BT input report (seed byte <c>0xA1</c>) — used by tests/fixtures.</summary>
    public static void WriteBluetoothInputCrc(Span<byte> report)
    {
        if (report.Length < 8) return;
        uint crc = HashWithSeed(0xA1, report[..^4]);
        BinaryPrimitives.WriteUInt32LittleEndian(report[^4..], crc);
    }

    /// <summary>Append CRC for BT output report (seed byte <c>0xA2</c>).</summary>
    public static void WriteBluetoothOutputCrc(Span<byte> report)
    {
        if (report.Length < 8) return;
        uint crc = HashWithSeed(0xA2, report[..^4]);
        BinaryPrimitives.WriteUInt32LittleEndian(report[^4..], crc);
    }

    static uint HashWithSeed(byte seed, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        crc = Table[(crc ^ seed) & 0xFF] ^ (crc >> 8);
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
