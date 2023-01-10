/// <summary>
///   A single byte header to differentiate raw packets (non-RPC).
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
