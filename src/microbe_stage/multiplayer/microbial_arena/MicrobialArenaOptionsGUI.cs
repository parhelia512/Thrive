using System.Collections.Generic;
using System.Linq;
using Godot;

public class MicrobialArenaOptionsGUI : MarginContainer, IGameModeOptionsMenu
{
    [Export]
    public NodePath BiomesPath = null!;

    private OptionButton biomes = null!;

    private List<Biome>? shownBiomes;

    public override void _Ready()
    {
        biomes = GetNode<OptionButton>(BiomesPath);

        shownBiomes = SimulationParameters.Instance.GetAllBiomes().ToList();

        foreach (var biome in shownBiomes)
        {
            biomes.AddItem(biome.Name);
        }
    }

    public IGameModeSettings ReadSettings()
    {
        return new MicrobialArenaSettings(shownBiomes?[biomes.Selected].InternalName ??
            SimulationParameters.Instance.GetBiome("tidepool").InternalName);
    }
}
