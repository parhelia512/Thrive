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
        MultiplayerGameWorld.Map.CurrentPatch?.Name ?? throw new InvalidOperationException("no current patch");

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

        foreach (var player in MultiplayerGameWorld.Players)
        {
            var entity = MultiplayerGameWorld.GetEntity(player.Value.EntityID);
            if (entity == null)
                continue;

            var microbe = (Microbe)entity;
            if (!microbe.IsInsideTree() || !MultiplayerGameWorld.Species.ContainsKey((uint)player.Key))
                continue;

            var species = MultiplayerGameWorld.GetSpecies((uint)player.Key);
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
        spawner = new MicrobialArenaSpawnSystem(rootOfDynamicallySpawned, MultiplayerGameWorld, Clouds, ArenaRadius);
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
            MultiplayerGameWorld.GetSpecies((uint)NetworkManager.Instance.PeerId!.Value)));
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

        var species = (MicrobeSpecies)MultiplayerGameWorld.GetSpecies((uint)peerId);
        NetworkManager.Instance.SetPlayerInfoFloats(peerId, "base_size", species.BaseHexSize);
        NotifyScore(peerId);
    }

    protected override void OnNetEntityReplicated(uint id, INetEntity entity, int serverEntityCount)
    {
        base.OnNetEntityReplicated(id, entity, serverEntityCount);

        // Only add the clouds if we're still joining i.e. client is in non-ready state to avoid double cloud
        // additions (normal cloud spawn synchronization is in CompoundCloudSystem.SyncCloudAddition)
        if (NetworkManager.Instance.PlayerInfo?.Status == NetPlayerStatus.Joining && entity is CloudBlob cloudBlob)
        {
            foreach (var cell in cloudBlob.Content)
            {
                Clouds.AddCloud(
                    cloudBlob.Compound, cell.Amount, new Vector3(cell.Position.x, 0, cell.Position.y));
            }
        }
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

        if (!MultiplayerGameWorld.Players.TryGetValue(peerId, out NetPlayerState state))
            return false;

        spawned = SpawnHelpers.SpawnNetworkedMicrobe(peerId, MultiplayerGameWorld.GetSpecies((uint)peerId),
            new Vector3(0, 0, 0), rootOfDynamicallySpawned, Clouds, spawner, CurrentGame!);

        spawned.OnDeath = OnPlayerDied;
        spawned.OnNetworkedDeathCompletes = OnPlayerDestroyed;
        spawned.OnKilledByPeer = OnPlayerKilled;

        state.EntityID = spawned.NetEntityId;
        state.RespawnTimer = Constants.PLAYER_RESPAWN_TIME;
        state.IsDead = false;
        MultiplayerGameWorld.Players[peerId] = state;

        SyncPlayerStateToAllPeers(peerId, state);

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
            MultiplayerGameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        // Update environment for process system
        ProcessSystem.SetBiome(MultiplayerGameWorld.Map.CurrentPatch.Biome);

        HUD.UpdateEnvironmentalBars(GameWorld.Map.CurrentPatch!.Biome);
    }

    /// <summary>
    ///   Game-mode specific score calculation.
    ///   Score is calculated from the number of kills + species base hex size.
    /// </summary>
    protected override int CalculateScore(int peerId)
    {
        var info = NetworkManager.Instance.GetPlayerInfo(peerId);
        if (info == null)
            return 0;

        info.Ints.TryGetValue("kills", out int kills);
        info.Floats.TryGetValue("base_size", out float size);

        return kills + (int)size;
    }

    private void HandlePlayersVisibility()
    {
        foreach (var player in MultiplayerGameWorld.Players)
        {
            var info = NetworkManager.Instance.GetPlayerInfo(player.Key);
            if (info == null)
                continue;

            var state = player.Value;
            var entity = MultiplayerGameWorld.GetEntity(state.EntityID);

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

            if (!MultiplayerGameWorld.Players.TryGetValue(player.Key, out NetPlayerState state))
                continue;

            if (!state.IsDead)
                continue;

            state.RespawnTimer -= delta;
            MultiplayerGameWorld.Players[player.Key] = state;

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
            MultiplayerGameWorld.Players.TryGetValue(player.PeerId!.Value, out NetPlayerState state);
            state.IsDead = true;
            MultiplayerGameWorld.Players[player.PeerId!.Value] = state;

            SyncPlayerStateToAllPeers(player.PeerId!.Value, state);

            var info = NetworkManager.Instance.GetPlayerInfo(player.PeerId.Value);
            if (info == null)
                return;

            info.Ints.TryGetValue("deaths", out int deaths);
            NetworkManager.Instance.SetPlayerInfoInts(player.PeerId.Value, "deaths", deaths + 1);
        }
    }

    private void OnPlayerKilled(int attackerId, int victimId, string source)
    {
        var attackerInfo = NetworkManager.Instance.GetPlayerInfo(attackerId);
        if (attackerInfo == null)
            return;

        attackerInfo.Ints.TryGetValue("kills", out int kills);
        NetworkManager.Instance.SetPlayerInfoInts(attackerId, "kills", kills + 1);
        NotifyScore(attackerId);

        Rpc(nameof(NotifyKill), attackerId, victimId, source);
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

        MultiplayerGameWorld.UpdateSpecies(deserialized.ID, deserialized);
        LocalPlayerSpeciesReceived?.Invoke();
    }

    [PuppetSync]
    private void NotifyKill(int attackerId, int victimId, string source)
    {
        HUD.SortScoreBoard();

        var attackerName = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(attackerId)!.Name}[/color]";
        var victimName = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(victimId)!.Name}[/color]";

        var ownId = NetworkManager.Instance.PeerId;
        bool highlight = attackerId == ownId || victimId == ownId;

        switch (source)
        {
            case "pilus":
                HUD.AddKillFeedLog($"{attackerName} ripped apart {victimName}", highlight);
                break;
            case "engulf":
                HUD.AddKillFeedLog($"{attackerName} engulfed {victimName}", highlight);
                break;
            case "toxin":
            case "oxytoxy":
                HUD.AddKillFeedLog($"{attackerName} fatally poisoned {victimName}", highlight);
                break;
        }
    }

    [Master]
    private void NotifyMovingToEditor()
    {
        var sender = GetTree().GetRpcSenderId();

        MultiplayerGameWorld.Players.TryGetValue(sender, out NetPlayerState state);
        state.InEditor = true;
        MultiplayerGameWorld.Players[sender] = state;
        SyncPlayerStateToAllPeers(sender, state);

        if (sender == GetTree().GetNetworkUniqueId())
        {
            LocalPlayerSpeciesReceived?.Invoke();
            return;
        }

        RpcId(sender, nameof(SyncSpecies),
            ThriveJsonConverter.Instance.SerializeObject(MultiplayerGameWorld.Species[(uint)sender]));
    }

    [Master]
    private void NotifyReturningFromEditor(string editedSpecies)
    {
        var sender = GetTree().GetRpcSenderId();

        MultiplayerGameWorld.Players.TryGetValue(sender, out NetPlayerState state);
        state.InEditor = false;
        MultiplayerGameWorld.Players[sender] = state;
        SyncPlayerStateToAllPeers(sender, state);

        // TODO: server-side check to make sure client aren't sending unnaturally edited species
        Rpc(nameof(SyncSpecies), editedSpecies);

        // Get the species's base hex size
        var deserialized = ThriveJsonConverter.Instance.DeserializeObject<MicrobeSpecies>(editedSpecies);
        if (deserialized != null)
            NetworkManager.Instance.SetPlayerInfoFloats(sender, "base_size", deserialized.BaseHexSize);

        NotifyScore(sender);
    }

    [Master]
    private void NotifyMicrobeSuicide(int peerId)
    {
        if (MultiplayerGameWorld.Players.TryGetValue(peerId, out NetPlayerState state))
        {
            var entity = MultiplayerGameWorld.GetEntity(state.EntityID);
            var microbe = (Microbe?)entity;
            microbe?.Damage(9999.0f, "suicide");
        }
    }
}
