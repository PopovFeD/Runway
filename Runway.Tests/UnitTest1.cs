using System.Text;
using Runway.Framing;
using Runway.Protocol;
using Runway.Tests.Support;

namespace Runway.Tests;

public class FrameAndCrcTests
{
    [Fact]
    public void Compute_ReturnsCrcForKnownPayload()
    {
        byte[] data = Encoding.ASCII.GetBytes("123456789");

        ushort crc = Crc16.Compute(data);

        Assert.Equal((ushort)0x4B37, crc);
    }

    [Fact]
    public void Compute_ReturnsMaxValueForEmptyPayload()
    {
        byte[] data = Array.Empty<byte>();

        ushort crc = Crc16.Compute(data);

        Assert.Equal((ushort)0xFFFF, crc);
    }

    [Fact]
    public void Append_ReturnsParsedFrame_WhenFrameIsComplete()
    {
        byte[] payload = { 0x01, 0x02, 0x03, 0x04 };
        byte[] frameBytes = FrameTestHelper.BuildFrameBytes(
            version: 0x10,
            messageType: 0x20,
            sequence: 0x1234,
            payload
        );

        var reader = new FrameReader();
        List<Frame> frames = reader.Append(frameBytes);

        var frame = Assert.Single(frames);
        Assert.Equal((byte)0x10, frame.Version);
        Assert.Equal((byte)0x20, frame.MessageType);
        Assert.Equal((ushort)0x1234, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }

    [Fact]
    public void Append_CollectsFrame_WhenBytesArriveInParts()
    {
        byte[] payload = { 0xAA, 0xBB };
        byte[] frameBytes = FrameTestHelper.BuildFrameBytes(
            version: 0x01,
            messageType: 0x02,
            sequence: 0x0007,
            payload
        );

        var reader = new FrameReader();

        byte[] firstPart = frameBytes.Take(frameBytes.Length - 2).ToArray();
        Assert.Empty(reader.Append(firstPart));

        List<Frame> frames = reader.Append(frameBytes.TakeLast(2).ToArray());

        var frame = Assert.Single(frames);
        Assert.Equal((byte)0x01, frame.Version);
        Assert.Equal((byte)0x02, frame.MessageType);
        Assert.Equal((ushort)0x0007, frame.Sequence);
        Assert.Equal(payload, frame.Payload);
    }
}
