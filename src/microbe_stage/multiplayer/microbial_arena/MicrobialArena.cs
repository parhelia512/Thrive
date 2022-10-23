using System;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   Main class for managing an online competitive game mode set in the microbial stage.
/// </summary>
[JsonObject(IsReference = true)]
[SceneLoadedClass("res://src/microbe_stage/multiplayer/microbial_arena/MicrobialArena.tscn")]
[DeserializedCallbackTarget]
[UseThriveSerializer]
public class MicrobialArena : MultiplayerStageBase<Microbe>
{
    private MicrobialArenaSpawnSystem spawner = null!;
    private MicrobeSystem microbeSystem = null!;
    private FloatingChunkSystem floatingChunkSystem = null!;

    public CompoundCloudSystem Clouds { get; private set; } = null!;
    public FluidSystem FluidSystem { get; private set; } = null!;
    public TimedLifeSystem TimedLifeSystem { get; private set; } = null!;
    public ProcessSystem ProcessSystem { get; private set; } = null!;
    public MicrobeCamera Camera { get; private set; } = null!;
    public MicrobialArenaHUD HUD { get; private set; } = null!;
    public PlayerHoverInfo HoverInfo { get; private set; } = null!;
    public PlayerMicrobialArenaInput PlayerInput { get; private set; } = null!;

    public float ArenaRadius { get; private set; } = 1000;

    public int MaxEntities { get; private set; } = Constants.MULTIPLAYER_MICROBIAL_ARENA_DEFAULT_ENTITY_LIMIT;

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
        floatingChunkSystem = new FloatingChunkSystem(rootOfDynamicallySpawned, Clouds);
        spawner = new MicrobialArenaSpawnSystem(
            rootOfDynamicallySpawned, GameWorld.Map.CurrentPatch!.Biome, ArenaRadius, MaxEntities, random);
    }

    public override void _Process(float delta)
    {
        base._Process(delta);
        microbeSystem.Process(delta);
        floatingChunkSystem.Process(delta, Player?.Translation);
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

        if (IsNetworkMaster())
            spawner.Init();

        Camera.SetBackground(SimulationParameters.Instance.GetBackground(
            GameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        // Update environment for process system
        ProcessSystem.SetBiome(GameWorld.Map.CurrentPatch.Biome);

        base.SetupStage();
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
    }

    protected override void OnLocalPlayerDespawn()
    {
        base.OnLocalPlayerDespawn();

        Camera.ObjectToFollow = null;
    }

    protected override bool HandlePlayerSpawn(int peerId, out Microbe? spawned)
    {
        spawned = HandlePlayerSpawn(peerId);
        spawned.OnNetworkedDeathCompletes = OnPlayerDestroyed;
        return true;
    }

    protected override bool HandlePlayerDespawn(Microbe removed)
    {
        if (removed.PhagocytosisStep == PhagocytosisPhase.None)
            removed.DestroyDetachAndQueueFree();

        return true;
    }

    protected override void UpdatePatchSettings(bool promptPatchNameChange = true)
    {
    }

    private Microbe HandlePlayerSpawn(int peerId)
    {
        var microbe = (Microbe)SpawnHelpers.SpawnNetworkedMicrobe(peerId, PlayerSpeciesList[peerId], new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, Clouds, spawner, CurrentGame!);
        return microbe;
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerDied(Microbe player)
    {
        Player = null;
        Camera.ObjectToFollow = null;
    }

    [DeserializedCallbackAllowed]
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
