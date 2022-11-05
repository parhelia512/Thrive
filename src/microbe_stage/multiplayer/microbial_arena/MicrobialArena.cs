using System;
using System.Collections.Generic;
using Godot;

/// <summary>
///   Main class for managing an online competitive game mode set in the microbial stage.
/// </summary>
public class MicrobialArena : MultiplayerStageBase<Microbe>
{
    /// <summary>
    ///   The mesh size that makes compound cloud plane works automagically at 1000 unit simulation size.
    /// </summary>
    public const int COMPOUND_PLANE_SIZE_MAGIC_NUMBER = 667;

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

    /// <summary>
    ///   NOTE: Changing this require adjusting <see cref="COMPOUND_PLANE_SIZE_MAGIC_NUMBER"/>!!!
    /// </summary>
    public int ArenaRadius { get; private set; } = 1000;

    public float MaxGameDuration => 60;

    public List<Vector2> SpawnCoordinates { get; set; } = new();

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

        // Start a new game if started directly from MicrobialArena.tscn
        CurrentGame ??= GameProperties.StartNewMicrobialArenaGame();

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
            TimedLifeSystem.Process(delta);
            ProcessSystem.Process(delta);
            spawner.Process(delta, Vector3.Zero);

            NetworkHandleRespawns(delta);
        }

        microbeSystem.Process(delta);
        floatingChunkSystem.Process(delta, null);

        HandlePlayersVisibility();

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

        StartNewGame();

        guiRoot = GetNode<Control>("GUI");
        HUD = guiRoot.GetNode<MicrobialArenaHUD>("MicrobialArenaHUD");
        HoverInfo = GetNode<PlayerHoverInfo>("PlayerHoverInfo");
        PlayerInput = GetNode<PlayerMicrobialArenaInput>("PlayerMicrobialArenaInput");
        Camera = world.GetNode<MicrobeCamera>("PrimaryCamera");
        Clouds = world.GetNode<CompoundCloudSystem>("CompoundClouds");

        TimedLifeSystem = new TimedLifeSystem(rootOfDynamicallySpawned);
        ProcessSystem = new ProcessSystem(rootOfDynamicallySpawned);
        microbeSystem = new MicrobeSystem(rootOfDynamicallySpawned);
        floatingChunkSystem = new FloatingChunkSystem(rootOfDynamicallySpawned, Clouds);
        FluidSystem = new FluidSystem(rootOfDynamicallySpawned);
        spawner = new MicrobialArenaSpawnSystem(rootOfDynamicallySpawned, MpGameWorld, Clouds, ArenaRadius);
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

    public override void StartNewGame()
    {
        CurrentGame = GameProperties.StartNewMicrobialArenaGame();
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
            MpGameWorld.GetSpecies((uint)NetworkManager.Instance.PeerId!.Value)));
    }

    public override void OnSuicide()
    {
        Rpc(nameof(NotifyMicrobeSuicide), NetworkManager.Instance.PeerId!.Value);
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        // Supply inactive fluid system just to fulfill init parameter
        Clouds.Init(FluidSystem, ArenaRadius, COMPOUND_PLANE_SIZE_MAGIC_NUMBER);

        // Disable clouds simulation as it's too chaotic to synchronize
        Clouds.RunSimulation = false;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            spawner.OnSpawnCoordinatesChanged = OnSpawnCoordinatesChanged;
            spawner.Init();
        }

        OnGameStarted();
    }

    protected override void OnGameStarted()
    {
        base.OnGameStarted();

        UpdatePatchSettings(false);
    }

    protected override void NetworkTick(float delta)
    {
    }

    protected override void RegisterPlayer(int peerId)
    {
        base.RegisterPlayer(peerId);

        if (peerId != NetworkManager.DEFAULT_SERVER_ID)
            RpcId(peerId, nameof(SyncSpawnCoordinates), SpawnCoordinates);
    }

    protected override void OnOwnPlayerSpawned(Microbe player)
    {
        base.OnOwnPlayerSpawned(player);

        player.AddToGroup(Constants.PLAYER_GROUP);

        if (NetworkManager.Instance.IsClient)
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
        spawned.OnKilledByPeer = OnPlayerKilled;

        state.EntityID = spawned.NetEntityId;
        state.RespawnTimer = Constants.PLAYER_RESPAWN_TIME;
        state.IsDead = false;
        MpGameWorld.Players[peerId] = state;

        SyncPlayerStateToAllPlayers(peerId, state);

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
        Camera.SetBackground(SimulationParameters.Instance.GetBackground(
            MpGameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        // Update environment for process system
        ProcessSystem.SetBiome(MpGameWorld.Map.CurrentPatch.Biome);

        HUD.UpdateEnvironmentalBars(GameWorld.Map.CurrentPatch!.Biome);
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

            SetEntityAsAttached(entity, info.Status == NetPlayerStatus.Active && !state.InEditor);
        }
    }

    private void NetworkHandleRespawns(float delta)
    {
        if (NetworkManager.Instance.IsClient)
            return;

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

    private void OnPlayerDied(Microbe player)
    {
        if (player.PeerId == NetworkManager.Instance.PeerId)
        {
            Player = null;
            Camera.ObjectToFollow = null;
        }

        if (NetworkManager.Instance.IsAuthoritative)
        {
            MpGameWorld.Players.TryGetValue(player.PeerId!.Value, out NetPlayerState state);
            state.IsDead = true;
            MpGameWorld.Players[player.PeerId!.Value] = state;

            SyncPlayerStateToAllPlayers(player.PeerId!.Value, state);

            var info = NetworkManager.Instance.GetPlayerInfo(player.PeerId.Value);
            if (info == null)
                return;

            info.Ints.TryGetValue("deaths", out int deaths);
            NetworkManager.Instance.SetPlayerInfoInts(player.PeerId.Value, "deaths", deaths + 1);
        }
    }

    private void OnPlayerKilled(int attackerPeerId, int victimPeerId, string source)
    {
        var attackerInfo = NetworkManager.Instance.GetPlayerInfo(attackerPeerId);
        if (attackerInfo == null)
            return;

        attackerInfo.Ints.TryGetValue("kills", out int kills);
        NetworkManager.Instance.SetPlayerInfoInts(attackerPeerId, "kills", kills + 1);

        Rpc(nameof(NotifyKill), attackerPeerId, victimPeerId, source);
    }

    private void OnPlayerReproductionStatusChanged(Microbe player, bool ready)
    {
        OnCanEditStatusChanged(ready && player.Colony == null);
    }

    private void OnPlayerDestroyed(int peerId)
    {
        DespawnPlayer(peerId);
    }

    private void OnSpawnCoordinatesChanged(List<Vector2> coordinates)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        SpawnCoordinates = coordinates;
        Rpc(nameof(SyncSpawnCoordinates), coordinates);
    }

    [Puppet]
    private void SyncSpawnCoordinates(List<Vector2> coordinates)
    {
        SpawnCoordinates = coordinates;
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

    [PuppetSync]
    private void NotifyKill(int attackerPeerId, int victimPeerId, string source)
    {
        HUD.SortScoreBoard();

        if (attackerPeerId != NetworkManager.Instance.PeerId)
            return;

        var victimName = NetworkManager.Instance.GetPlayerInfo(victimPeerId)!.Name;

        switch (source)
        {
            case "pilus":
                HUD.AddKillFeedLog($"Ripped apart [color=yellow]{victimName}[/color]");
                break;
            case "engulf":
                HUD.AddKillFeedLog($"Engulfed [color=yellow]{victimName}[/color]");
                break;
            case "toxin":
            case "oxytoxy":
                HUD.AddKillFeedLog($"Fatally poisoned [color=yellow]{victimName}[/color]");
                break;
        }
    }

    [Master]
    private void NotifyMovingToEditor()
    {
        var sender = GetTree().GetRpcSenderId();

        MpGameWorld.Players.TryGetValue(sender, out NetPlayerState state);
        state.InEditor = true;
        MpGameWorld.Players[sender] = state;
        SyncPlayerStateToAllPlayers(sender, state);

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
        SyncPlayerStateToAllPlayers(sender, state);

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
