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
    private Control guiRoot = null!;
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

    public Action? LocalPlayerSpeciesReceived { get; set; }

    public float ArenaRadius { get; private set; } = 1000;

    public int MaxEntities { get; private set; } = Constants.MULTIPLAYER_MICROBIAL_ARENA_DEFAULT_ENTITY_LIMIT;

    [JsonIgnore]
    public bool Visible
    {
        set
        {
            var casted = (Spatial)world;
            casted.Visible = value;
            guiRoot.Visible = value;
        }
    }

    protected override IStageHUD BaseHUD => HUD;

    protected override string StageLoadingMessage => "Joining Microbial Arena";

    private LocalizedString CurrentPatchName =>
        MpGameWorld.Map.CurrentPatch?.Name ?? throw new InvalidOperationException("no current patch");

    public override void _Ready()
    {
        base._Ready();

        ResolveNodeReferences();

        HUD.Init(this);
        HoverInfo.Init(Camera, Clouds);
        PlayerInput.Init(this);

        SetupStage();
    }

    public override void _Process(float delta)
    {
        if (!NodeReferencesResolved)
            return;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            microbeSystem.Process(delta);
            floatingChunkSystem.Process(delta, Player?.Translation);

            // TODO: replicate these systems
            TimedLifeSystem.Process(delta);
            ProcessSystem.Process(delta);

            // if (Player != null)
            //     spawner.Process(delta, Player.GlobalTranslation);

            HandlePlayersVisibility();

            NetworkHandleRespawns(delta);
        }

        if (HasPlayer)
            Camera.ObjectToFollow = Player?.IsInsideTree() == false ? null : Player;

        foreach (var player in MpGameWorld.Players)
        {
            var entity = MpGameWorld.GetEntity(player.Value.EntityID);
            if (entity == null)
                continue;

            var microbe = (Microbe)entity;
            if (!microbe.IsInsideTree() || !MpGameWorld.Species.ContainsKey((uint)player.Key))
                continue;

            var species = MpGameWorld.GetSpecies((uint)player.Key);
            if (microbe.Species != species)
                microbe.ApplySpecies(species);
        }
    }

    public override void ResolveNodeReferences()
    {
        if (NodeReferencesResolved)
            return;

        base.ResolveNodeReferences();

        CurrentGame = NetworkManager.Instance.CurrentGame;

        guiRoot = GetNode<Control>("GUI");
        HUD = guiRoot.GetNode<MicrobialArenaHUD>("MicrobialArenaHUD");
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
            rootOfDynamicallySpawned, MpGameWorld.Map.CurrentPatch!.Biome, ArenaRadius, MaxEntities, random);
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
        if (Player?.Dead != false)
        {
            GD.PrintErr("Player object disappeared or died while transitioning to the editor");
            return;
        }

        if (CurrentGame == null)
            throw new InvalidOperationException("Stage has no current game");

        Node sceneInstance;

        // Might be related to saving but somehow the editor button can be enabled while in a colony
        // TODO: for now to prevent crashing, we just ignore that here, but this should be fixed by the button
        // becoming disabled properly
        // https://github.com/Revolutionary-Games/Thrive/issues/2504
        if (Player.Colony != null)
        {
            GD.PrintErr("Editor button was enabled and pressed while the player is in a colony");
            return;
        }

        var scene = SceneManager.Instance.LoadScene(MultiplayerGameState.MicrobialArenaEditor);

        sceneInstance = scene.Instance();
        var editor = (MicrobialArenaEditor)sceneInstance;

        editor.CurrentGame = CurrentGame;
        editor.ReturnToStage = this;

        // Stage must NOT be detached from the tree
        Visible = false;
        AddChild(editor);

        MovingToEditor = false;

        Rpc(nameof(NotifyMovingToEditor));
    }

    public override void OnReturnFromEditor()
    {
        Visible = true;

        BaseHUD.OnEnterStageTransition(false, true);
        BaseHUD.HideReproductionDialog();

        StartMusic();

        Rpc(nameof(NotifyReturningFromEditor), ThriveJsonConverter.Instance.SerializeObject(
            MpGameWorld.GetSpecies((uint)GetTree().GetNetworkUniqueId())));
    }

    public override void OnSuicide()
    {
        Rpc(nameof(NotifyMicrobeSuicide), GetTree().GetNetworkUniqueId());
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        Clouds.Init(FluidSystem);

        if (NetworkManager.Instance.IsAuthoritative)
            spawner.Init();

        Camera.SetBackground(SimulationParameters.Instance.GetBackground(
            MpGameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        // Update environment for process system
        ProcessSystem.SetBiome(MpGameWorld.Map.CurrentPatch.Biome);

        OnGameStarted();
    }

    protected override void NetworkTick(float delta)
    {
    }

    protected override void OnOwnPlayerSpawned(Microbe player)
    {
        base.OnOwnPlayerSpawned(player);

        player.AddToGroup(Constants.PLAYER_GROUP);

        if (NetworkManager.Instance.IsPuppet)
            player.OnDeath = OnPlayerDied;

        player.OnReproductionStatus = OnPlayerReproductionStatusChanged;

        Camera.ObjectToFollow = player;
    }

    protected override void OnOwnPlayerDespawn()
    {
        base.OnOwnPlayerDespawn();

        Camera.ObjectToFollow = null;
    }

    protected override bool HandlePlayerSpawn(int peerId, out Microbe? spawned)
    {
        spawned = null;

        if (!MpGameWorld.Players.TryGetValue(peerId, out NetPlayerState state))
            return false;

        spawned = SpawnHelpers.SpawnNetworkedMicrobe(peerId, MpGameWorld.GetSpecies((uint)peerId),
            new Vector3(0, 0, 0), rootOfDynamicallySpawned, Clouds, spawner, CurrentGame!);

        spawned.OnDeath = OnPlayerDied;
        spawned.OnNetworkedDeathCompletes = OnPlayerDestroyed;

        state.EntityID = spawned.NetEntityId;
        state.RespawnTimer = Constants.PLAYER_RESPAWN_TIME;
        state.IsDead = false;
        MpGameWorld.Players[peerId] = state;

        NetworkManager.Instance.SyncPlayerStateToAllPlayers(peerId, state);

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

    private void HandlePlayersVisibility()
    {
        foreach (var player in MpGameWorld.Players)
        {
            var info = NetworkManager.Instance.GetPlayerInfo(player.Key);
            if (info == null)
                continue;

            var state = player.Value;
            var entity = MpGameWorld.GetEntity(state.EntityID);

            if (entity == null)
                continue;

            var microbe = (Microbe)entity;

            if (microbe.IsInsideTree() && (info.Status != NetPlayerStatus.Active || state.InEditor))
            {
                // Hide player from the rest of the world
                Rpc(nameof(SetEntityAsAttached), state.EntityID, false);
            }
            else if (!microbe.IsInsideTree() && info.Status == NetPlayerStatus.Active && !state.InEditor)
            {
                Rpc(nameof(SetEntityAsAttached), state.EntityID, true);
            }
        }
    }

    private void NetworkHandleRespawns(float delta)
    {
        foreach (var player in NetworkManager.Instance.PlayerList)
        {
            if (player.Value.Status != NetPlayerStatus.Active)
                continue;

            if (!MpGameWorld.Players.TryGetValue(player.Key, out NetPlayerState state))
                continue;

            if (!state.IsDead)
                continue;

            state.RespawnTimer -= delta;
            MpGameWorld.Players[player.Key] = state;

            // Respawn the player once the timer is up
            if (state.RespawnTimer <= 0)
            {
                SpawnPlayer(player.Key);
            }
        }
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerDied(Microbe player)
    {
        if (player.PeerId == NetworkManager.Instance.PeerId)
        {
            Player = null;
            Camera.ObjectToFollow = null;
        }

        MpGameWorld.Players.TryGetValue(player.PeerId!.Value, out NetPlayerState state);
        state.IsDead = true;
        MpGameWorld.Players[player.PeerId!.Value] = state;

        NetworkManager.Instance.SyncPlayerStateToAllPlayers(player.PeerId!.Value, state);
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerReproductionStatusChanged(Microbe player, bool ready)
    {
        OnCanEditStatusChanged(ready && player.Colony == null);
    }

    [DeserializedCallbackAllowed]
    private void OnPlayerDestroyed(int peerId)
    {
        DespawnPlayer(peerId);
    }

    [PuppetSync]
    private void SyncSpecies(string serialized)
    {
        var deserialized = ThriveJsonConverter.Instance.DeserializeObject<MicrobeSpecies>(serialized);
        if (deserialized == null)
        {
            NetworkManager.Instance.Print("Error while trying to deserialize incoming MicrobeSpecies");
            return;
        }

        MpGameWorld.UpdateSpecies(deserialized.ID, deserialized);
        LocalPlayerSpeciesReceived?.Invoke();
    }

    [Master]
    private void NotifyMovingToEditor()
    {
        var sender = GetTree().GetRpcSenderId();

        MpGameWorld.Players.TryGetValue(sender, out NetPlayerState state);
        state.InEditor = true;
        MpGameWorld.Players[sender] = state;
        NetworkManager.Instance.SyncPlayerStateToAllPlayers(sender, state);

        if (sender == GetTree().GetNetworkUniqueId())
        {
            LocalPlayerSpeciesReceived?.Invoke();
            return;
        }

        RpcId(sender, nameof(SyncSpecies),
            ThriveJsonConverter.Instance.SerializeObject(MpGameWorld.Species[(uint)sender]));
    }

    [Master]
    private void NotifyReturningFromEditor(string editedSpecies)
    {
        var sender = GetTree().GetRpcSenderId();

        MpGameWorld.Players.TryGetValue(sender, out NetPlayerState state);
        state.InEditor = false;
        MpGameWorld.Players[sender] = state;
        NetworkManager.Instance.SyncPlayerStateToAllPlayers(sender, state);

        // TODO: server-side check to make sure client aren't sending unnaturally edited species
        Rpc(nameof(SyncSpecies), editedSpecies);
    }

    [Master]
    private void NotifyMicrobeSuicide(int peerId)
    {
        if (MpGameWorld.Players.TryGetValue(peerId, out NetPlayerState state))
        {
            var entity = MpGameWorld.GetEntity(state.EntityID);
            var microbe = (Microbe?)entity;
            microbe?.Damage(9999.0f, "suicide");
        }
    }
}
