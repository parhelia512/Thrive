/// <summary>
///   Used as a single byte header in raw packets to differentiate the incoming packets' type.
/// </summary>
public enum RawPacketFlag
{
    Ping,
    Pong,
}
