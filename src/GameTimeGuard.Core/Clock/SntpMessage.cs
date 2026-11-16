using System.Buffers.Binary;

namespace GameTimeGuard.Core.Clock;

/// <summary>
/// 最小 SNTP 报文。服务在能联网时用来对照墙钟，解析逻辑可在没有网络的环境里测试。
/// </summary>
public static class SntpMessage
{
    public const uint UnixEpochSeconds = 2_208_988_800;

    public static byte[] CreateRequest()
    {
        var buffer = new byte[48];
        buffer[0] = 0x1B;
        return buffer;
    }

    public static bool TryReadTransmitTimestamp(ReadOnlySpan<byte> packet, out DateTimeOffset utc)
    {
        utc = default;
        if (packet.Length < 48)
            return false;
        var mode = packet[0] & 0x07;
        if (mode is not (4 or 5))
            return false;

        var seconds = BinaryPrimitives.ReadUInt32BigEndian(packet[40..]);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(packet[44..]);
        if (seconds == 0)
            return false;

        var epoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        utc = epoch.AddSeconds(seconds).AddSeconds(fraction / 4294967296d);
        return true;
    }
}
