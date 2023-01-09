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

    /// <summary>
    ///   Specifies how long the "round" end screen (scoreboard) be displayed before the game exits.
    ///   NOTE: 32 syncs nicely with the music.
    /// </summary>
    public const int GAME_OVER_SCREEN_DURATION = 32;

    private Control guiRoot = null!;
    private MicrobialArenaSpawnSystem spawner = null!;
    private MicrobeSystem microbeSystem = null!;
    private FloatingChunkSystem floatingChunkSystem = null!;

    private float gameOverExitTimer;

    public CompoundCloudSystem Clouds { get; private set; } = null!;
    public FluidSystem FluidSystem { get; private set; } = null!;
    public TimedLifeSystem TimedLifeSystem { get; private set; } = null!;
    public ProcessSystem ProcessSystem { get; private set; } = null!;
    public MicrobeCamera Camera { get; private set; } = null!;
    public MicrobialArenaHUD HUD { get; private set; } = null!;
    public PlayerHoverInfo HoverInfo { get; private set; } = null!;
    public MultiplayerMicrobeStageInput PlayerInput { get; private set; } = null!;

    public Action? LocalPlayerSpeciesReceived { get; set; }

    public MicrobialArenaSettings Settings =>
        NetworkManager.Instance.Settings?.GameModeSettings as MicrobialArenaSettings ??
        throw new InvalidOperationException("Microbial arena settings not set");

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

    protected override string StageLoadingMessage => TranslationServer.Translate("JOINING_MICROBIAL_ARENA");

    private LocalizedString CurrentPatchName =>
        MultiplayerGameWorld.Map.CurrentPatch?.Name ?? throw new InvalidOperationException("no current patch");

    public override void _Ready()
    {
        base._Ready();

        // Start a new game if started directly from MicrobialArena.tscn
        CurrentGame ??= GameProperties.StartNewMicrobialArenaGame(
            SimulationParameters.Instance.GetBiome(Settings.BiomeType));

        ResolveNodeReferences();

        HUD.Init(this);
        HoverInfo.Init(Camera, Clouds);
        PlayerInput.Init(this);

        SetupStage();
    }

    public override void _Process(float delta)
    {
        base._Process(delta);

        if (!NodeReferencesResolved)
            return;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            TimedLifeSystem.Process(delta);
            ProcessSystem.Process(delta);
            spawner.Process(delta, Vector3.Zero);

            NetworkHandleRespawns(delta);

            if (IsGameOver() && gameOverExitTimer > 0)
            {
                gameOverExitTimer -= delta;

                if (gameOverExitTimer <= 0)
                {
                    PauseManager.Instance.Resume("ArenaGameOver");
                    NetworkManager.Instance.Exit();
                }
            }
        }

        // We need these to be run on the client also since both systems run some logic that's better be handled
        // client-side.
        microbeSystem.Process(delta);
        floatingChunkSystem.Process(delta, null);

        HandlePlayersVisibility();

        if (HasPlayer)
            Camera.ObjectToFollow = Player?.IsInsideTree() == false ? null : Player;

        // Apply species
        foreach (var player in MultiplayerGameWorld.PlayerVars)
        {
            if (!MultiplayerGameWorld.TryGetNetworkEntity(player.Value.EntityId, out INetworkEntity entity))
                return;

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
        PlayerInput = GetNode<MultiplayerMicrobeStageInput>("PlayerMicrobialArenaInput");
        Camera = world.GetNode<MicrobeCamera>("PrimaryCamera");
        Clouds = world.GetNode<CompoundCloudSystem>("CompoundClouds");

        if (NetworkManager.Instance.IsAuthoritative)
        {
            TimedLifeSystem = new TimedLifeSystem(rootOfDynamicallySpawned);
            ProcessSystem = new ProcessSystem(rootOfDynamicallySpawned);
            spawner = new MicrobialArenaSpawnSystem(
                rootOfDynamicallySpawned, MultiplayerGameWorld, Clouds, Settings.ArenaRadius);
        }

        microbeSystem = new MicrobeSystem(rootOfDynamicallySpawned);
        floatingChunkSystem = new FloatingChunkSystem(rootOfDynamicallySpawned, Clouds);
        FluidSystem = new FluidSystem(rootOfDynamicallySpawned);
    }

    public override void StartMusic()
    {
        Jukebox.Instance.PlayCategory("MicrobeStage");
    }

    public override void StartNewGame()
    {
        CurrentGame = GameProperties.StartNewMicrobialArenaGame(
            SimulationParameters.Instance.GetBiome(Settings.BiomeType));
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

        var gameMode = SimulationParameters.Instance.GetMultiplayerGameMode("MicrobialArena");
        var scene = SceneManager.Instance.LoadScene(gameMode.EditorScene!);

        sceneInstance = scene.Instance();
        var editor = (MicrobialArenaEditor)sceneInstance;

        editor.CurrentGame = CurrentGame;
        editor.ReturnToStage = this;

        // Stage must NOT be detached from the tree
        Visible = false;
        AddChild(editor);

        MovingToEditor = false;

        RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerNotifyMovingToEditor));
    }

    public override void OnReturnFromEditor()
    {
        Visible = true;

        BaseHUD.OnEnterStageTransition(false, true);
        BaseHUD.HideReproductionDialog();

        StartMusic();

        var buffer = new PackedBytesBuffer();
        MultiplayerGameWorld.GetSpecies((uint)NetworkManager.Instance.PeerId).NetworkSerialize(buffer);
        RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerNotifyReturningFromEditor), buffer.Data);
    }

    public override void OnSuicide()
    {
        RpcId(NetworkManager.DEFAULT_SERVER_ID, nameof(ServerNotifyMicrobeSuicide), NetworkManager.Instance.PeerId);
    }

    protected override void SetupStage()
    {
        base.SetupStage();

        // Supply inactive fluid system just to fulfill init parameter
        Clouds.Init(FluidSystem, Settings.ArenaRadius, COMPOUND_PLANE_SIZE_MAGIC_NUMBER);

        // Disable clouds simulation as it's currently too chaotic to synchronize
        Clouds.RunSimulation = false;

        if (NetworkManager.Instance.IsAuthoritative)
        {
            spawner.OnSpawnCoordinatesChanged = OnSpawnCoordinatesChanged;
            spawner.Init();
        }

        OnGameStarted();

        StartMusic();
    }

    protected override void OnGameStarted()
    {
        base.OnGameStarted();

        UpdatePatchSettings(false);
    }

    protected override void GameOver()
    {
        base.GameOver();

        gameOverExitTimer = GAME_OVER_SCREEN_DURATION;

        if (NetworkManager.Instance.IsAuthoritative)
            PauseManager.Instance.AddPause("ArenaGameOver");

        HUD.ToggleInfoScreen();

        Jukebox.Instance.PlayCategory("MicrobialArenaEnd");
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
        NetworkManager.Instance.SetVar(peerId, "base_size", species.BaseHexSize);
        NotifyScoreUpdate(peerId);
    }

    protected override void OnLocalPlayerSpawned(Microbe player)
    {
        base.OnLocalPlayerSpawned(player);

        player.AddToGroup(Constants.PLAYER_GROUP);

        if (NetworkManager.Instance.IsClient)
            player.OnDeath = OnPlayerDied;

        player.OnReproductionStatus = OnPlayerReproductionStatusChanged;

        Camera.ObjectToFollow = player;
    }

    protected override void OnLocalPlayerDespawned()
    {
        base.OnLocalPlayerDespawned();

        Camera.ObjectToFollow = null;
    }

    protected override bool HandlePlayerSpawn(int peerId, out Microbe? spawned)
    {
        spawned = null;

        if (!MultiplayerGameWorld.PlayerVars.TryGetValue(peerId, out NetworkPlayerVars vars))
            return false;

        spawned = SpawnHelpers.SpawnMicrobe(MultiplayerGameWorld.GetSpecies((uint)peerId), Vector3.Zero,
            rootOfDynamicallySpawned, SpawnHelpers.LoadMicrobeScene(), false, Clouds, spawner, CurrentGame!, null,
            peerId);

        spawned.OnDeath = OnPlayerDied;
        spawned.OnNetworkDeathFinished = OnPlayerDestroyed;
        spawned.OnKilledByAnotherPlayer = OnPlayerKilled;

        vars.EntityId = spawned.NetworkEntityId;
        vars.SetVar("respawn", Constants.PLAYER_RESPAWN_TIME);
        vars.SetVar("editor", false);
        vars.SetVar("dead", false);
        ServerSyncPlayerVars(peerId);

        return true;
    }

    protected override bool HandlePlayerDespawn(Microbe removed)
    {
        if (removed.PhagocytosisStep == PhagocytosisPhase.None)
            removed.DestroyDetachAndQueueFree();

        return true;
    }

    protected override Species CreateNewSpeciesForPlayer(int peerId)
    {
        // Pretend that each separate Species instance across players are LUCA
        var species = new MicrobeSpecies((uint)peerId, "Primum", "thrivium");
        GameWorld.SetInitialSpeciesProperties(species);
        return species;
    }

    [Puppet]
    protected override void SyncSpecies(int peerId, byte[] serialized)
    {
        var buffer = new PackedBytesBuffer(serialized);
        var species = new MicrobeSpecies();
        species.NetworkDeserialize(buffer);

        MultiplayerGameWorld.UpdateSpecies(peerId, species);
        LocalPlayerSpeciesReceived?.Invoke();
    }

    protected override void UpdatePatchSettings(bool promptPatchNameChange = true)
    {
        Camera.SetBackground(SimulationParameters.Instance.GetBackground(
            MultiplayerGameWorld.Map.CurrentPatch!.BiomeTemplate.Background));

        if (NetworkManager.Instance.IsAuthoritative)
        {
            // Update environment for process system
            ProcessSystem.SetBiome(MultiplayerGameWorld.Map.CurrentPatch.Biome);
        }

        HUD.UpdateEnvironmentalBars(GameWorld.Map.CurrentPatch!.Biome);
    }

    /// <summary>
    ///   <inheritdoc/> Score is calculated from the number of kills + species base hex size.
    /// </summary>
    protected override int CalculateScore(int peerId)
    {
        var info = NetworkManager.Instance.GetPlayerInfo(peerId);
        if (info == null)
            return 0;

        info.TryGetVar("kills", out int kills);
        info.TryGetVar("base_size", out float size);

        return kills + (int)size;
    }

    private void HandlePlayersVisibility()
    {
        foreach (var player in MultiplayerGameWorld.PlayerVars)
        {
            var info = NetworkManager.Instance.GetPlayerInfo(player.Key);
            if (info == null)
                continue;

            var vars = player.Value;

            if (MultiplayerGameWorld.TryGetNetworkEntity(vars.EntityId, out INetworkEntity entity))
            {
                bool toAttach = info.Status == NetworkPlayerStatus.Active && !vars.GetVar<bool>("editor");
                SetEntityAsAttached(entity, toAttach);
            }
        }
    }

    private void NetworkHandleRespawns(float delta)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        foreach (var player in NetworkManager.Instance.ConnectedPlayers)
        {
            if (player.Value.Status != NetworkPlayerStatus.Active)
                continue;

            if (!MultiplayerGameWorld.PlayerVars.TryGetValue(player.Key, out NetworkPlayerVars vars))
                continue;

            if (!vars.TryGetVar("dead", out bool dead) || !vars.TryGetVar("respawn", out float timer))
                continue;

            if (!dead)
                continue;

            var diff = timer - delta;
            vars.SetVar("respawn", diff);
            MultiplayerGameWorld.PlayerVars[player.Key] = vars;

            // Respawn the player once the timer is up
            if (diff <= 0)
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
            ServerSetPlayerVar(player.PeerId, "dead", true);

            var info = NetworkManager.Instance.GetPlayerInfo(player.PeerId);
            if (info == null)
                return;

            info.TryGetVar("deaths", out int deaths);
            NetworkManager.Instance.SetVar(player.PeerId, "deaths", deaths + 1);
        }
    }

    private void OnPlayerKilled(int attackerId, int victimId, string source)
    {
        var attackerInfo = NetworkManager.Instance.GetPlayerInfo(attackerId);
        if (attackerInfo == null)
            return;

        attackerInfo.TryGetVar("kills", out int kills);
        NetworkManager.Instance.SetVar(attackerId, "kills", kills + 1);
        NotifyScoreUpdate(attackerId);

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
    private void NotifyKill(int attackerId, int victimId, string source)
    {
        HUD.SortScoreBoard();

        var attackerName = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(attackerId)!.Name}[/color]";
        var victimName = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(victimId)!.Name}[/color]";

        var ownId = NetworkManager.Instance.PeerId;
        bool highlight = attackerId == ownId || victimId == ownId;

        // TODO: Make this more extensible
        string content = string.Empty;
        switch (source)
        {
            case "pilus":
                content = TranslationServer.Translate("KILL_FEED_RIPPED_APART");
                break;
            case "engulf":
                content = TranslationServer.Translate("KILL_FEED_ENGULFED");
                break;
            case "toxin":
            case "oxytoxy":
                content = TranslationServer.Translate("KILL_FEED_POISONED");
                break;
        }

        HUD.AddKillFeedLog(content.FormatSafe(attackerName, victimName), highlight);
    }

    [PuppetSync]
    private void ClientNotifyReturningFromEditor(int peerId)
    {
        var name = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(peerId)!.Name}[/color]";

        HUD.AddKillFeedLog(TranslationServer.Translate("KILL_FEED_EVOLVED").FormatSafe(name),
            peerId == NetworkManager.Instance.PeerId);
    }

    [PuppetSync]
    private void ClientNotifyMicrobeSuicide(int peerId)
    {
        var name = $"[color=yellow]{NetworkManager.Instance.GetPlayerInfo(peerId)!.Name}[/color]";

        HUD.AddKillFeedLog(TranslationServer.Translate("KILL_FEED_SUICIDE").FormatSafe(name),
            peerId == NetworkManager.Instance.PeerId);
    }

    [RemoteSync]
    private void ServerNotifyMovingToEditor()
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var sender = GetTree().GetRpcSenderId();

        ServerSetPlayerVar(sender, "editor", true);

        if (sender == NetworkManager.Instance.PeerId)
        {
            // We're the server, no need to sync stuff
            LocalPlayerSpeciesReceived?.Invoke();
            return;
        }

        var buffer = new PackedBytesBuffer();
        MultiplayerGameWorld.Species[(uint)sender].NetworkSerialize(buffer);
        RpcId(sender, nameof(SyncSpecies), sender, buffer.Data);
    }

    [RemoteSync]
    private void ServerNotifyReturningFromEditor(byte[] editedSpecies)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        var sender = GetTree().GetRpcSenderId();

        ServerSetPlayerVar(sender, "editor", false);

        // TODO: server-side check to make sure client aren't sending unnaturally edited species
        Rpc(nameof(SyncSpecies), sender, editedSpecies);

        // Need to access the base hex size
        var buffer = new PackedBytesBuffer(editedSpecies);
        var species = new MicrobeSpecies();
        species.NetworkDeserialize(buffer);

        MultiplayerGameWorld.UpdateSpecies(species.ID, species);
        NetworkManager.Instance.SetVar(sender, "base_size", species.BaseHexSize);

        NotifyScoreUpdate(sender);

        Rpc(nameof(ClientNotifyReturningFromEditor), sender);
    }

    [RemoteSync]
    private void ServerNotifyMicrobeSuicide(int peerId)
    {
        if (NetworkManager.Instance.IsClient)
            return;

        if (TryGetPlayer(peerId, out Microbe player) && !player.Dead)
        {
            player.Damage(9999.0f, "suicide");
            Rpc(nameof(ClientNotifyMicrobeSuicide), peerId);
        }
    }
}
