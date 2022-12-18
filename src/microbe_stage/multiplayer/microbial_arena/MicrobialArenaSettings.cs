[JSONDynamicTypeAllowed]
public class MicrobialArenaSettings : IGameModeSettings
{
    public MicrobialArenaSettings(float timeLimit, string biomeType)
    {
        TimeLimit = timeLimit;
        BiomeType = biomeType;
    }

    public float TimeLimit { get; set; }

    /// <summary>
    ///   NOTE: Changing this require adjusting <see cref="MicrobialArena.COMPOUND_PLANE_SIZE_MAGIC_NUMBER"/>!!!
    /// </summary>
    public int ArenaRadius { get; set; } = 1000;

    public string BiomeType { get; set; }

    public override string ToString()
    {
        return $"Game time: {TimeLimit}\nArena Radius: {ArenaRadius}\nBiome: {BiomeType}";
    }
}
