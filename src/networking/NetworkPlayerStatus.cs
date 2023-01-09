/// <summary>
///   Status of a networked player in relation to the current game instance.
/// </summary>
public enum NetworkPlayerStatus
{
    Lobby,

    Joining,

    /// <summary>
    ///   Player is set up and can actively engage in the gameplay.
    /// </summary>
    Active,

    Leaving,
}
