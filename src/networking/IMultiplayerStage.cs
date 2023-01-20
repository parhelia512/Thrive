using System;

/// <summary>
///   Multiplayer equivalent of <see cref="IStage"/>.
/// </summary>
public interface IMultiplayerStage : IStage
{
    /// <summary>
    ///   Should be emitted when the multiplayer stage is done setting up.
    /// </summary>
    public event EventHandler? GameReady;

    public bool TryGetPlayer(int peerId, out NetworkCharacter player);
}
