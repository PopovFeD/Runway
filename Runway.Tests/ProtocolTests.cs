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
    public void ParseControl_ReturnsControlPacketWithType()
    {
        var frame = new Frame(1, (byte)MessageType.Ping, 3, Array.Empty<byte>());

        var packet = Assert.IsType<ControlPacket>(PacketParser.Parse(frame));

        Assert.Equal(MessageType.Ping, packet.Type);
    }

    [Fact]
    public void EnvironmentPacket_BuildThenParse_Roundtrips()
    {
        // 1013.25 гПа = 101325 Па — не влезает в ushort, поэтому payload на uint32;
        // roundtrip через Build+Parse заодно фиксирует совпадение эндианности
        var frame = PacketBuilder.CreateEnvironment(1, 42, 1013.25, 347.5);

        Assert.Equal((byte)MessageType.Environment, frame.MessageType);
        Assert.Equal(8, frame.Payload.Length);

        var packet = Assert.IsType<EnvironmentPacket>(PacketParser.Parse(frame));

        Assert.Equal(1013.25, packet.PressureHpa);
        Assert.Equal(347.5, packet.LightLux);
    }

    [Fact]
    public void ParseEnvironment_InvalidLength_ShouldThrow()
    {
        var frame = new Frame(1, (byte)MessageType.Environment, 1, new byte[] { 1, 2, 3 });

        Assert.Throws<ArgumentException>(() => PacketParser.Parse(frame));
    }

    [Fact]
    public void CreateTelemetry_ShouldEncodeValues()
    {
        var frame = PacketBuilder.CreateTelemetry(1, 10, 24.53, 51.28);

        Assert.Equal((byte)MessageType.Telemetry, frame.MessageType);

        Assert.Equal(new byte[] { 0x95, 0x09, 0x08, 0x14 }, frame.Payload);
    }
}
