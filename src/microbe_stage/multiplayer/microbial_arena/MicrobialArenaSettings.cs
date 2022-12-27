[JSONDynamicTypeAllowed]
public class MicrobialArenaSettings : IGameModeSettings
{
    public MicrobialArenaSettings(string biomeType)
    {
        BiomeType = biomeType;
    }

    /// <summary>
    ///   A plain constructor for network serialization/deserialization purposes.
    /// </summary>
    public MicrobialArenaSettings()
    {
        BiomeType = string.Empty;
    }

    /// <summary>
    ///   NOTE: Changing this require adjusting <see cref="MicrobialArena.COMPOUND_PLANE_SIZE_MAGIC_NUMBER"/>!!!
    /// </summary>
    public int ArenaRadius { get; set; } = 1000;

    public string BiomeType { get; set; }

    public void NetworkSerialize(PackedBytesBuffer buffer)
    {
        buffer.Write(ArenaRadius);
        buffer.Write(BiomeType);
    }

    public void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        ArenaRadius = buffer.ReadInt32();
        BiomeType = buffer.ReadString();
    }

    public override string ToString()
    {
        return $"Arena Radius: {ArenaRadius}\nBiome: {BiomeType}";
    }
}
