using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

public class MicrobialArenaHUD : MultiplayerStageHUDBase<MicrobialArena>
{
    [Export]
    public NodePath IngestedMatterBarPath = null!;

    [Export]
    public PackedScene WinBoxScene = null!;

    [Export]
    public NodePath BindingModeHotkeyPath = null!;

    [Export]
    public NodePath UnbindAllHotkeyPath = null!;

    private ActionButton bindingModeHotkey = null!;
    private ActionButton unbindAllHotkey = null!;

    private ProgressBar ingestedMatterBar = null!;

    private CustomDialog? winBox;

    /// <summary>
    ///   If not null the signaling agent radial menu is open for the given microbe, which should be the player
    /// </summary>
    private Microbe? signalingAgentMenuOpenForMicrobe;

    private int? playerColonySize;

    private bool playerWasDigested;

    // These signals need to be copied to inheriting classes for Godot editor to pick them up
    [Signal]
    public new delegate void OnOpenMenu();

    [Signal]
    public new delegate void OnOpenMenuToHelp();

    protected override string? UnPauseHelpText => TranslationServer.Translate("PAUSE_PROMPT");

    public override void _Ready()
    {
        base._Ready();
        ingestedMatterBar = GetNode<ProgressBar>(IngestedMatterBarPath);

        bindingModeHotkey = GetNode<ActionButton>(BindingModeHotkeyPath);
        unbindAllHotkey = GetNode<ActionButton>(UnbindAllHotkeyPath);
    }

    public void ShowSignalingCommandsMenu(Microbe player)
    {
        if (packControlRadial.Visible)
        {
            GD.PrintErr("Radial menu is already open for signaling commands");
            return;
        }

        var choices = new List<(string Text, int Id)>
        {
            (TranslationServer.Translate("SIGNAL_COMMAND_NONE"), (int)MicrobeSignalCommand.None),
            (TranslationServer.Translate("SIGNAL_COMMAND_FOLLOW"), (int)MicrobeSignalCommand.FollowMe),
            (TranslationServer.Translate("SIGNAL_COMMAND_TO_ME"), (int)MicrobeSignalCommand.MoveToMe),
            (TranslationServer.Translate("SIGNAL_COMMAND_FLEE"), (int)MicrobeSignalCommand.FleeFromMe),
            (TranslationServer.Translate("SIGNAL_COMMAND_AGGRESSION"), (int)MicrobeSignalCommand.BecomeAggressive),
        };

        packControlRadial.Radial.CenterText = TranslationServer.Translate("SIGNAL_TO_EMIT");

        signalingAgentMenuOpenForMicrobe = player;
        packControlRadial.ShowWithItems(choices);
    }

    public MicrobeSignalCommand? SelectSignalCommandIfOpen()
    {
        // Return nothing if not open
        if (!packControlRadial.Visible)
            return null;

        var item = packControlRadial.Radial.HoveredItem;

        packControlRadial.Hide();

        if (item == null)
            return null;

        return (MicrobeSignalCommand)item.Value;
    }

    /// <summary>
    ///   Applies a signaling command to microbe. This is here as the user can actively select a radial menu item
    /// </summary>
    /// <param name="command">The command to apply</param>
    /// <param name="microbe">The target microbe</param>
    public void ApplySignalCommand(MicrobeSignalCommand? command, Microbe microbe)
    {
        microbe.QueuedSignalingCommand = command;
        signalingAgentMenuOpenForMicrobe = null;
    }

    public void ToggleWinBox()
    {
        if (winBox != null)
        {
            winExtinctBoxHolder.Hide();
            winBox.DetachAndQueueFree();
            return;
        }

        winExtinctBoxHolder.Show();

        winBox = WinBoxScene.Instance<CustomDialog>();
        winExtinctBoxHolder.AddChild(winBox);
        winBox.Show();

        winBox.GetNode<Timer>("Timer").Connect("timeout", this, nameof(ToggleWinBox));
    }

    protected override void ReadPlayerHitpoints(out float hp, out float maxHP)
    {
        hp = stage!.Player!.Hitpoints;
        maxHP = stage.Player.MaxHitpoints;
    }

    protected override void UpdateHealth(float delta)
    {
        if (stage?.Player != null && stage.Player.PhagocytosisStep != PhagocytosisPhase.Ingested)
        {
            playerWasDigested = false;
            healthBar.TintProgress = defaultHealthBarColour;
            base.UpdateHealth(delta);
            return;
        }

        float hp = 0;

        string hpText = playerWasDigested ?
            TranslationServer.Translate("DEVOURED") :
            hp.ToString(CultureInfo.CurrentCulture);

        // Update to the player's current digested progress, unless the player does not exist
        if (stage!.HasPlayer)
        {
            var percentageValue = TranslationServer.Translate("PERCENTAGE_VALUE");

            // Show the digestion progress to the player
            hp = 1 - (stage.Player!.DigestedAmount / Constants.PARTIALLY_DIGESTED_THRESHOLD);
            maxHP = Constants.FULLY_DIGESTED_LIMIT;
            hpText = percentageValue.FormatSafe(Mathf.Round((1 - hp) * 100));
            playerWasDigested = true;
            FlashHealthBar(new Color(0.96f, 0.5f, 0.27f), delta);
        }

        healthBar.MaxValue = maxHP;
        GUICommon.SmoothlyUpdateBar(healthBar, hp, delta);
        hpLabel.Text = hpText;
        hpLabel.HintTooltip = hpText;
    }

    protected override CompoundBag? GetPlayerUsefulCompounds()
    {
        return stage!.Player?.Compounds;
    }

    protected override Func<Compound, bool> GetIsUsefulCheck()
    {
        var colony = stage!.Player!.Colony;
        if (colony == null)
        {
            var compounds = stage.Player.Compounds;
            return compound => compounds.IsUseful(compound);
        }

        return compound => colony.ColonyMembers.Any(c => c.Compounds.IsUseful(compound));
    }

    protected override bool SpecialHandleBar(ProgressBar bar)
    {
        if (bar == ingestedMatterBar)
        {
            bar.Visible = GetPlayerUsedIngestionCapacity() > 0;
            return true;
        }

        return false;
    }

    protected override bool ShouldShowAgentsPanel()
    {
        var colony = stage!.Player!.Colony;
        if (colony == null)
        {
            return GetPlayerUsefulCompounds()!.AreAnySpecificallySetUseful(allAgents);
        }

        return colony.ColonyMembers.Any(
            c => c.Compounds.AreAnySpecificallySetUseful(allAgents));
    }

    protected override ICompoundStorage GetPlayerStorage()
    {
        return stage!.Player!.Colony?.ColonyCompounds ?? (ICompoundStorage)stage.Player.Compounds;
    }

    protected override void UpdateCompoundBars(float delta)
    {
        base.UpdateCompoundBars(delta);

        ingestedMatterBar.MaxValue = stage!.Player!.Colony?.HexCount ?? stage.Player.HexCount;
        GUICommon.SmoothlyUpdateBar(ingestedMatterBar, GetPlayerUsedIngestionCapacity(), delta);
        ingestedMatterBar.GetNode<Label>("Value").Text = ingestedMatterBar.Value + " / " + ingestedMatterBar.MaxValue;
    }

    protected override ProcessStatistics? GetPlayerProcessStatistics()
    {
        return stage!.Player!.ProcessStatistics;
    }

    protected override void CalculatePlayerReproductionProgress(out Dictionary<Compound, float> gatheredCompounds,
        out Dictionary<Compound, float> totalNeededCompounds)
    {
        stage!.Player!.CalculateReproductionProgress(out gatheredCompounds, out totalNeededCompounds);
    }

    protected override IEnumerable<(bool Player, Species Species)> GetHoveredSpecies()
    {
        return stage!.HoverInfo.HoveredMicrobes.Select(m => (m.IsPlayerMicrobe, m.Species));
    }

    protected override IReadOnlyDictionary<Compound, float> GetHoveredCompounds()
    {
        return stage!.HoverInfo.HoveredCompounds;
    }

    protected override string GetMouseHoverCoordinateText()
    {
        return TranslationServer.Translate("STUFF_AT")
            .FormatSafe(stage!.Camera.CursorWorldPos.x, stage.Camera.CursorWorldPos.z);
    }

    protected override void UpdateAbilitiesHotBar()
    {
        var player = stage!.Player!;

        bool showToxin;
        bool showSlime;

        // Multicellularity is not checked here (only colony membership) as that is also not checked when firing toxins
        if (player.Colony != null)
        {
            showToxin = player.Colony.ColonyMembers.Any(c => c.AgentVacuoleCount > 0);
            showSlime = player.Colony.ColonyMembers.Any(c => c.SlimeJets.Count > 0);
        }
        else
        {
            showToxin = player.AgentVacuoleCount > 0;
            showSlime = player.SlimeJets.Count > 0;
        }

        UpdateBaseAbilitiesBar(!player.CellTypeProperties.MembraneType.CellWall, showToxin, showSlime,
            player.HasSignalingAgent, player.State == Microbe.MicrobeState.Engulf);

        bindingModeHotkey.Visible = player.CanBind;
        unbindAllHotkey.Visible = player.CanUnbind;

        bindingModeHotkey.Pressed = player.State == Microbe.MicrobeState.Binding;
        unbindAllHotkey.Pressed = Input.IsActionPressed(unbindAllHotkey.ActionName);
    }

    private void OnRadialItemSelected(int itemId)
    {
        if (signalingAgentMenuOpenForMicrobe != null)
        {
            ApplySignalCommand((MicrobeSignalCommand)itemId, signalingAgentMenuOpenForMicrobe);
            return;
        }

        GD.PrintErr("Unexpected radial menu item selection signal");
    }

    private float GetPlayerUsedIngestionCapacity()
    {
        return stage!.Player!.Colony?.UsedIngestionCapacity ?? stage.Player.UsedIngestionCapacity;
    }
}
