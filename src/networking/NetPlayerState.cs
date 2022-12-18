/// <summary>
///   Describes the state of a player in-game.
/// </summary>
public struct NetPlayerState
{
    public uint EntityID;
    public float RespawnTimer;
    public bool IsDead;
    public bool InEditor;
}
