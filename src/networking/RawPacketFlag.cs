/// <summary>
///   A single byte header to differentiate raw packets (non-RPC).
/// </summary>
public enum RawPacketFlag
{
    /// <summary>
    ///   Client-to-server.
    /// </summary>
    Ping,

    /// <summary>
    ///   Server-to-client.
    /// </summary>
    Pong,
}
