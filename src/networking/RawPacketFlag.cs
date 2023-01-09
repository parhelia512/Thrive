/// <summary>
///   A single byte header in raw packets (non-RPC) to differentiate their type.
/// </summary>
public enum RawPacketFlag
{
    /// <summary>
    ///   Server-to-client.
    /// </summary>
    Ping,

    /// <summary>
    ///   Client-to-server.
    /// </summary>
    Pong,
}
