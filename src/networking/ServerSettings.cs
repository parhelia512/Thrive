using System;
using Godot;

/// <summary>
///   Settings shared both by the server and the client.
/// </summary>
public class ServerSettings : INetworkSerializable
{
    /// <summary>
    ///   The server's name.
    /// </summary>
    public string Name { get; set; } = TranslationServer.Translate("N_A");

    public string Address { get; set; } = Constants.LOCAL_HOST;

    public int Port { get; set; } = Constants.MULTIPLAYER_DEFAULT_PORT;

    public int MaxPlayers { get; set; } = Constants.MULTIPLAYER_DEFAULT_MAX_PLAYERS;

    public float SessionLength { get; set; } = Constants.MULTIPLAYER_DEFAULT_SESSION_LENGTH;

    /// <inheritdoc cref="NetworkManager.TimeStep"/>
    public float TimeStep { get; set; } = Constants.DEFAULT_SERVER_TIME_STEP_SECONDS;

    public bool UseUpnp { get; set; }

    public MultiplayerGameMode? SelectedGameMode { get; set; }

    public IGameModeSettings? GameModeSettings { get; set; }

    public void NetworkSerialize(PackedBytesBuffer buffer)
    {
        buffer.Write(Name);
        buffer.Write(Address);
        buffer.Write(Port);
        buffer.Write(MaxPlayers);
        buffer.Write(SessionLength);
        buffer.Write(TimeStep);
        buffer.Write(UseUpnp);

        var bools = new bool[2] { SelectedGameMode != null, GameModeSettings != null };
        buffer.Write(bools.ToByte());

        if (bools[0])
            buffer.Write((ushort)SelectedGameMode!.Index);

        if (bools[1])
        {
            var packed = new PackedBytesBuffer();
            GameModeSettings!.NetworkSerialize(packed);
            buffer.Write(GameModeSettings.GetType().Name);
            buffer.Write(packed);
        }
    }

    public void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        Name = buffer.ReadString();
        Address = buffer.ReadString();
        Port = buffer.ReadInt32();
        MaxPlayers = buffer.ReadInt32();
        SessionLength = buffer.ReadSingle();
        TimeStep = buffer.ReadSingle();
        UseUpnp = buffer.ReadBoolean();

        var bools = buffer.ReadByte();

        if (bools.ToBoolean(0))
            SelectedGameMode = SimulationParameters.Instance.GetMultiplayerGameModeByIndex(buffer.ReadUInt16());

        if (bools.ToBoolean(1))
        {
            var type = Type.GetType($"{buffer.ReadString()}, Thrive");
            var packed = buffer.ReadBuffer();

            if (type == null)
                throw new InvalidOperationException("Type is not valid");

            var settings = (IGameModeSettings)Activator.CreateInstance(type);
            settings.NetworkDeserialize(packed);
            GameModeSettings = settings;
        }
    }

    public override string ToString()
    {
        return $"(Name: {Name}, Address: {Address}, Port: {Port}, MaxPlayers: {MaxPlayers})";
    }
}
