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
}
