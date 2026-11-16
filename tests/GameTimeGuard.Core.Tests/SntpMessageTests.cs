using System.Buffers.Binary;
using GameTimeGuard.Core.Clock;

namespace GameTimeGuard.Core.Tests;

public class SntpMessageTests
{
    [Fact]
    public void Reads_a_server_transmit_timestamp()
    {
        var packet = new byte[48];
        packet[0] = 0x24;
        var unix = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(40), (uint)(unix + SntpMessage.UnixEpochSeconds));

        Assert.True(SntpMessage.TryReadTransmitTimestamp(packet, out var utc));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), utc);
    }

    [Fact]
    public void Rejects_a_client_request_packet()
    {
        var request = SntpMessage.CreateRequest();
        Assert.False(SntpMessage.TryReadTransmitTimestamp(request, out _));
    }
}
