using System.Globalization;
using Godot;

public class MicrobialArena : MultiplayerStageBase<Microbe>
{
    private PatchManager patchManager = null!;

    public CompoundCloudSystem Clouds { get; private set; } = null!;

    public FluidSystem FluidSystem { get; private set; } = null!;

    public TimedLifeSystem TimedLifeSystem { get; private set; } = null!;

    public ProcessSystem ProcessSystem { get; private set; } = null!;

    public MicrobeCamera Camera { get; private set; } = null!;

    public MicrobialArenaHUD HUD { get; private set; } = null!;

    public PlayerHoverInfo HoverInfo { get; private set; } = null!;

    public PlayerMicrobialArenaInput PlayerInput { get; private set; } = null!;

    private SpawnSystem spawner = null!;

    protected override IStageHUD BaseHUD => HUD;

    public override void _Ready()
    {
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

        CurrentGame ??= GameProperties.StartNewMicrobeGame(new WorldGenerationSettings());

        base.ResolveNodeReferences();

        HUD = GetNode<MicrobialArenaHUD>("MicrobialArenaHUD");
        HoverInfo = GetNode<PlayerHoverInfo>("PlayerHoverInfo");
        PlayerInput = GetNode<PlayerMicrobialArenaInput>("PlayerMicrobialArenaInput");
        Camera = world.GetNode<MicrobeCamera>("PrimaryCamera");
        Clouds = world.GetNode<CompoundCloudSystem>("CompoundClouds");

        TimedLifeSystem = new TimedLifeSystem(rootOfDynamicallySpawned);
        ProcessSystem = new ProcessSystem(rootOfDynamicallySpawned);
        spawner = new SpawnSystem(rootOfDynamicallySpawned);
        patchManager = new PatchManager(spawner, ProcessSystem, Clouds, TimedLifeSystem,
            worldLight, CurrentGame);
    }

    public override void _Process(float delta)
    {
        base._Process(delta);

        TimedLifeSystem.Process(delta);
        ProcessSystem.Process(delta);
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
        Player?.Damage(9999.0f, "suicide");
    }

    public override void StartNewGame()
    {
        CurrentGame = GameProperties.StartNewMicrobeGame(new WorldGenerationSettings());

        base.StartNewGame();
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
        patchManager.CurrentGame = CurrentGame;

        UpdatePatchSettings(false);

        SpawnPlayer();
    }

    protected override void NetworkUpdateGameState()
    {
        foreach (var peer in Peers)
        {
            if (IsNetworkMaster())
            {
                peer.Value.Sync();
            }
            else
            {
                peer.Value.Send();
            }
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

    protected override void SpawnPlayer()
    {
        if (HasPlayer || NetworkManager.Instance.IsDedicated)
            return;

        var id = GetTree().GetNetworkUniqueId();
        SpawnPeer(id);
        Player = Peers[id];

        Player.AddToGroup(Constants.PLAYER_GROUP);

        Camera.ObjectToFollow = Player;

        if (spawnedPlayer)
        {
            // Random location on respawn
            Player.Translation = new Vector3(
                random.Next(Constants.MIN_SPAWN_DISTANCE, Constants.MAX_SPAWN_DISTANCE), 0,
                random.Next(Constants.MIN_SPAWN_DISTANCE, Constants.MAX_SPAWN_DISTANCE));
        }

        playerRespawnTimer = Constants.PLAYER_RESPAWN_TIME;
    }

    protected override void OnPeerSpawn(int peerId, out Microbe spawned)
    {
        spawned = (Microbe)SpawnHelpers.SpawnMicrobe(GameWorld.PlayerSpecies, new Vector3(0, 0, 0),
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!);
        spawned.Name = peerId.ToString(CultureInfo.CurrentCulture);
        spawned.SetupPlayerClient(peerId);
    }

    protected override void OnPeerDespawn(Microbe removed)
    {
        removed.DestroyDetachAndQueueFree();
    }
}
