/// <summary>
///   Status for a networked player in relation to a game instance.
/// </summary>
public enum NetPlayerStatus
{
    Lobby,

    Joining,

    /// <summary>
    ///   Player is set up and can actively engage in the gameplay.
    /// </summary>
    Active,

    Leaving,
}
