using System;
using Godot;

/// <summary>
///   Main class for managing an online competitive game mode set in the microbial stage.
/// </summary>
public class MicrobialArena : MultiplayerStageBase<Microbe>
{
    private NetworkedSpawnSystem spawner = null!;
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
        spawner = new NetworkedSpawnSystem(rootOfDynamicallySpawned);
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

        Camera.SetBackground(SimulationParameters.Instance.GetBackground(
            GameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        // Update environment for process system
        ProcessSystem.SetBiome(GameWorld.Map.CurrentPatch.Biome);
    }

    protected override void NetworkUpdateGameState(float delta)
    {
        base.NetworkUpdateGameState(delta);

        // TODO: replicate these systems
        TimedLifeSystem.Process(delta);
        ProcessSystem.Process(delta);

        // if (Player != null)
        //     spawner.Process(delta, Player.GlobalTranslation);
    }

    protected override void OnLocalPlayerSpawned(Microbe player)
    {
        base.OnLocalPlayerSpawned(player);

        player.AddToGroup(Constants.PLAYER_GROUP);
        player.OnDeath = OnPlayerDied;
        Camera.ObjectToFollow = player;

        if (IsNetworkMaster())
            player.OnNetworkedDeathCompletes = OnPlayerDestroyed;
    }

    protected override bool CreatePlayer(int peerId, out Microbe? spawned)
    {
        spawned = CreatePlayer(peerId);
        return true;
    }

    protected override bool DestroyPlayer(Microbe removed)
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

    protected override void UpdatePatchSettings(bool promptPatchNameChange = true)
    {
    }

    private Microbe CreatePlayer(int peerId)
    {
        var microbe = (Microbe)SpawnHelpers.SpawnMicrobe(PlayerSpeciesList[peerId], new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!);
        microbe.SetupNetworked(peerId);
        return microbe;
    }

    private void OnPlayerDied(Microbe player)
    {
        Player = null;
        Camera.ObjectToFollow = null;
    }

    private void OnPlayerDestroyed(int peerId)
    {
        DespawnPlayer(peerId);
    }

    [Master]
    private void SuicideReceived(int peerId)
    {
        Players[peerId].Value?.Damage(9999.0f, "suicide");
    }
}
