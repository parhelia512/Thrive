using Godot;

public interface INetworkInputBatch : INetworkSerializable
{
    public Vector3 LookAtPoint { get; set; }
    public Vector3 MovementDirection { get; set; }
}
