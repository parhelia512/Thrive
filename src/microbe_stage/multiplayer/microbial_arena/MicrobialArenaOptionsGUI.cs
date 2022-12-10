using System.Collections.Generic;
using System.Linq;
using Godot;

public class MicrobialArenaOptionsGUI : MarginContainer, IGameModeOptionsMenu
{
    [Export]
    public NodePath TimeLimitPath = null!;

    [Export]
    public NodePath BiomesPath = null!;

    private SpinBox timeLimit = null!;
    private OptionButton biomes = null!;

    private List<Biome>? shownBiomes;

    public override void _Ready()
    {
        timeLimit = GetNode<SpinBox>(TimeLimitPath);
        biomes = GetNode<OptionButton>(BiomesPath);

        shownBiomes = SimulationParameters.Instance.GetAllBiomes().ToList();

        foreach (var biome in shownBiomes)
        {
            biomes.AddItem(biome.Name);
        }
    }

    public IGameModeSettings ReadSettings()
    {
        return new MicrobialArenaSettings((float)timeLimit.Value, shownBiomes?[biomes.Selected].InternalName ??
            SimulationParameters.Instance.GetBiome("tidepool").InternalName);
    }
}
