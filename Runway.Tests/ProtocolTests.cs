using Runway.Framing;
using Runway.Protocol;
using Xunit;

namespace Runway.Tests.Protocol;

public class PacketParserTests
{
    [Fact]
    public void ParseTelemetry_ShouldDecodeValues()
    {
        var frame = new Frame(
            1,
            (byte)MessageType.Telemetry,
            1,
            new byte[] { 0x95, 0x09, 0x08, 0x14 }
        );

        var packet = (TelemetryPacket)PacketParser.Parse(frame);

        Assert.Equal(24.53, packet.Temperature);
        Assert.Equal(51.28, packet.Humidity);
    }

    [Fact]
    public void ParseTelemetry_InvalidLength_ShouldThrow()
    {
        var frame = new Frame(1, (byte)MessageType.Telemetry, 1, new byte[] { 1, 2 });

        Assert.Throws<ArgumentException>(() => PacketParser.Parse(frame));
    }

    [Fact]
    public void CreatePing_ShouldCreateEmptyPayload()
    {
        var frame = PacketBuilder.CreatePing(1, 15);

        Assert.Equal((byte)MessageType.Ping, frame.MessageType);
        Assert.Empty(frame.Payload);
    }

    [Fact]
    public void CreateTelemetry_ShouldEncodeValues()
    {
        var frame = PacketBuilder.CreateTelemetry(1, 10, 24.53, 51.28);

        Assert.Equal((byte)MessageType.Telemetry, frame.MessageType);

        Assert.Equal(new byte[] { 0x95, 0x09, 0x08, 0x14 }, frame.Payload);
    }
}
