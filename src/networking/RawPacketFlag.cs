/// <summary>
///   A single byte header for differentiating raw packets (non-RPC).
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

    /// <summary>
    ///   A server heartbeat packet for a multiplayer world.
    /// </summary>
    WorldHeartbeat,

    /// <summary>
    ///   A client's input packet.
    /// </summary>
    InputBatch,
}
