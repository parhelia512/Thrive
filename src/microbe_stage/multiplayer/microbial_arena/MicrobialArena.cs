using System;
using System.Globalization;
using Godot;

/// <summary>
///   Main class for managing an online competitive game mode set in the microbial stage.
/// </summary>
public class MicrobialArena : MultiplayerStageBase<Microbe>
{
    private PatchManager patchManager = null!;
    private SpawnSystem spawner = null!;
    private MicrobeSystem microbeSystem = null!;

    public CompoundCloudSystem Clouds { get; private set; } = null!;
    public FluidSystem FluidSystem { get; private set; } = null!;
    public TimedLifeSystem TimedLifeSystem { get; private set; } = null!;
    public ProcessSystem ProcessSystem { get; private set; } = null!;
    public MicrobeCamera Camera { get; private set; } = null!;
    public MicrobialArenaHUD HUD { get; private set; } = null!;
    public PlayerHoverInfo HoverInfo { get; private set; } = null!;
    public PlayerMicrobialArenaInput PlayerInput { get; private set; } = null!;

    protected override IStageHUD BaseHUD => HUD;

    private LocalizedString CurrentPatchName =>
        GameWorld.Map.CurrentPatch?.Name ?? throw new InvalidOperationException("no current patch");

    public override void _Ready()
    {
        base._Ready();

        ResolveNodeReferences();

        HUD.Init(this);
        HoverInfo.Init(Camera, Clouds);
        PlayerInput.Init(this);

        SetupStage();
    }

    public override void ResolveNodeReferences()
    {
        if (NodeReferencesResolved)
            return;

        CurrentGame = NetworkManager.Instance.CurrentGame;

        base.ResolveNodeReferences();

        HUD = GetNode<MicrobialArenaHUD>("MicrobialArenaHUD");
        HoverInfo = GetNode<PlayerHoverInfo>("PlayerHoverInfo");
        PlayerInput = GetNode<PlayerMicrobialArenaInput>("PlayerMicrobialArenaInput");
        Camera = world.GetNode<MicrobeCamera>("PrimaryCamera");
        Clouds = world.GetNode<CompoundCloudSystem>("CompoundClouds");

        Clouds.SetProcess(false);

        TimedLifeSystem = new TimedLifeSystem(rootOfDynamicallySpawned);
        ProcessSystem = new ProcessSystem(rootOfDynamicallySpawned);
        microbeSystem = new MicrobeSystem(rootOfDynamicallySpawned);
        spawner = new SpawnSystem(rootOfDynamicallySpawned);
        patchManager = new PatchManager(spawner, ProcessSystem, Clouds, TimedLifeSystem,
            worldLight, CurrentGame);
    }

    public override void _Process(float delta)
    {
        base._Process(delta);
        microbeSystem.Process(delta);
    }

    public override void OnFinishTransitioning()
    {
        base.OnFinishTransitioning();
        HUD.ShowPatchName(CurrentPatchName.ToString());
    }

    public override void StartMusic()
    {
        Jukebox.Instance.PlayCategory("MicrobeStage");
    }

    /// <summary>
    ///   Switches to the editor
    /// </summary>
    public override void MoveToEditor()
    {
    }

    public override void OnSuicide()
    {
        Rpc(nameof(SuicideReceived), GetTree().GetNetworkUniqueId());
    }

    protected override void SetupStage()
    {
        // Initialise the cloud system first so we can apply patch-specific brightness in OnGameStarted
        Clouds.Init(FluidSystem);

        // Initialise spawners next, since this removes existing spawners if present
        spawner.Init();

        base.SetupStage();
    }

    protected override void OnGameStarted()
    {
        base.OnGameStarted();

        patchManager.CurrentGame = CurrentGame;

        UpdatePatchSettings(false);
    }

    protected override void NetworkUpdateGameState(float delta)
    {
        // TODO: replicate these systems
        TimedLifeSystem.Process(delta);
        ProcessSystem.Process(delta);

        foreach (var player in Players)
        {
            var microbe = player.Value.Value;
            microbe?.NetworkSync();
        }
    }

    protected override void UpdatePatchSettings(bool promptPatchNameChange = true)
    {
        // TODO: would be nice to skip this if we are loading a save made in the editor as this gets called twice when
        // going back to the stage
        if (patchManager.ApplyChangedPatchSettingsIfNeeded(GameWorld.Map.CurrentPatch!))
        {
            Player?.ClearEngulfedObjects();
        }

        HUD.UpdateEnvironmentalBars(GameWorld.Map.CurrentPatch!.Biome);
    }

    protected override bool OnPlayerSpawn(int peerId, out Microbe? spawned)
    {
        spawned = (Microbe)SpawnHelpers.SpawnMicrobe(PlayerSpeciesList[peerId], new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!);
        spawned.Name = peerId.ToString(CultureInfo.CurrentCulture);
        spawned.SetupNetworked(peerId);

        if (peerId == GetTree().GetNetworkUniqueId())
        {
            spawned.AddToGroup(Constants.PLAYER_GROUP);
            spawned.OnDeath = OnPlayerDied;
            Camera.ObjectToFollow = spawned;
            spawnedPlayer = true;
        }

        if (IsNetworkMaster())
        {
            spawned.OnNetworkedDeathCompletes = OnPlayerDestroyed;
        }

        return true;
    }

    protected override bool OnPlayerDeSpawn(Microbe removed)
    {
        if (Player == removed)
        {
            Player = null;
            Camera.ObjectToFollow = null;
        }

        if (removed.PhagocytosisStep == PhagocytosisPhase.None)
            removed.DestroyDetachAndQueueFree();

        return true;
    }

    private void OnPlayerDied(Microbe player)
    {
        Player = null;
        Camera.ObjectToFollow = null;
    }

    private void OnPlayerDestroyed(int peerId)
    {
        Rpc(nameof(DeSpawnPlayer), peerId);
    }

    [Master]
    private void SuicideReceived(int peerId)
    {
        Players[peerId].Value?.Damage(9999.0f, "suicide");
    }
}
