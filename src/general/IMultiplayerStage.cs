using System;

/// <summary>
///   Multiplayer equivalent of <see cref="IStage"/>.
/// </summary>
public interface IMultiplayerStage : IStage
{
    public event EventHandler? GameReady;
}
