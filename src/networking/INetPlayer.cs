using System.Collections.Generic;

/// <summary>
///   A networked entity the player can control.
/// </summary>
public interface INetPlayer : INetEntity
{
    /// <summary>
    ///   The unique network ID self-assigned by the client. In gameplay context, this is used to differentiate
    ///   between player-character entities versus normal in-game entities.
    /// </summary>
    int? PeerId { get; }

    /// <summary>
    ///   Called server-side for every network tick.
    /// </summary>
    public void OnNetworkInput(Dictionary<string, string> data);

    /// <summary>
    ///   A naive implementation for marshaling entity inputs to be sent to the server.
    /// </summary>
    /// <remarks>
    ///   TODO: can this possibly be optimized to be far more efficient?
    /// </remarks>
    public Dictionary<string, string>? PackInputs();
}
