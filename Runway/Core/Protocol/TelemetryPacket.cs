namespace Runway.Protocol;

public sealed record TelemetryPacket : Packet
{
    public double Temperature { get; init; }

    public double Humidity { get; init; }
}
