using System;
using System.Collections.Generic;
using Godot;

public class PlayerMicrobialArenaInput : MultiplayerInputBase<MicrobialArena, NetworkMicrobeInput>
{
    private Random random = new();

    private NetworkMicrobeInput cachedInput;

    private Dictionary<int, NetworkMicrobeInput> serverInputs = new();

    public override void _Process(float delta)
    {
        if (stage == null)
            return;

        foreach (var input in serverInputs)
        {
            if (!stage.TryGetPlayer(input.Key, out Microbe player))
                continue;

            RunInput(player, input.Value, delta);
        }
    }

    // TODO: when using controller movement this should be screen relative movement by default
    [RunOnAxis(new[] { "g_move_forward", "g_move_backwards" }, new[] { -1.0f, 1.0f })]
    [RunOnAxis(new[] { "g_move_left", "g_move_right" }, new[] { -1.0f, 1.0f })]
    [RunOnAxisGroup(InvokeAlsoWithNoInput = true)]
    public void OnMovement(float delta, float forwardMovement, float leftRightMovement)
    {
        _ = delta;
        const float epsilon = 0.01f;

        // Reset auto move if a key was pressed
        if (Math.Abs(forwardMovement) + Math.Abs(leftRightMovement) > epsilon)
        {
            autoMove = false;
        }

        if (stage!.Player != null)
        {
            if (stage.Player.State == Microbe.MicrobeState.Unbinding)
            {
                stage.Player.MovementDirection = Vector3.Zero;
                return;
            }

            var movement = new Vector3(leftRightMovement, 0, forwardMovement);

            // TODO: change this line to only normalize when length exceeds 1 to make slowly moving with a controller
            // work
            var direction = autoMove ? new Vector3(0, 0, -1) : movement.Normalized();

            cachedInput.MovementDirection = direction;
            cachedInput.LookAtPoint = stage.Camera.CursorWorldPos;
        }
    }

    [RunOnKeyDown("g_fire_toxin")]
    public void EmitToxin()
    {
        cachedInput.EmitToxin = true;
    }

    [RunOnKeyDown("g_secrete_slime")]
    public bool SecreteSlime()
    {
        cachedInput.SecreteSlime = true;
        return false;
    }

    [RunOnKeyUp("g_secrete_slime")]
    public void StopSecretingSlime()
    {
        cachedInput.SecreteSlime = false;
    }

    [RunOnKeyDown("g_toggle_engulf")]
    public void ToggleEngulf()
    {
        cachedInput.Engulf = !cachedInput.Engulf;
    }

    [RunOnKeyChange("g_toggle_scoreboard")]
    public void ShowInfoScreen(bool heldDown)
    {
        stage?.HUD.ToggleInfoScreen();
    }

    [RunOnKeyChange("g_toggle_map")]
    public void ShowMap(bool heldDown)
    {
        stage?.HUD.ToggleMap();
    }

    [RunOnKey("g_cheat_glucose")]
    public void CheatGlucose(float delta)
    {
        if (Settings.Instance.CheatsEnabled)
        {
            SpawnCheatCloud("glucose", delta);
        }
    }

    [RunOnKey("g_cheat_ammonia")]
    public void CheatAmmonia(float delta)
    {
        if (Settings.Instance.CheatsEnabled)
        {
            SpawnCheatCloud("ammonia", delta);
        }
    }

    [RunOnKey("g_cheat_phosphates")]
    public void CheatPhosphates(float delta)
    {
        if (Settings.Instance.CheatsEnabled)
        {
            SpawnCheatCloud("phosphates", delta);
        }
    }

    [RunOnKeyDown("g_cheat_editor")]
    public void CheatEditor()
    {
        if (Settings.Instance.CheatsEnabled)
        {
            stage!.HUD.ShowReproductionDialog();
        }
    }

    protected override NetworkMicrobeInput SampleInput()
    {
        var cached = cachedInput;

        // Immediately reset the states for one-press inputs
        cachedInput.EmitToxin = false;

        return cached;
    }

    protected override void ApplyInput(int peerId, NetworkMicrobeInput input)
    {
        serverInputs[peerId] = input;
    }

    private void SpawnCheatCloud(string name, float delta)
    {
        SpawnHelpers.SpawnCloud(stage!.Clouds, stage.Camera.CursorWorldPos,
            SimulationParameters.Instance.GetCompound(name), Constants.CLOUD_CHEAT_DENSITY * delta, random);
    }

    private void RunInput(Microbe player, NetworkMicrobeInput input, float delta)
    {
        player.LookAtPoint = input.LookAtPoint;
        player.MovementDirection = input.MovementDirection;

        if (input.EmitToxin)
            player.QueueEmitToxin(SimulationParameters.Instance.GetCompound("oxytoxy"));

        if (input.SecreteSlime)
            player.QueueSecreteSlime(delta);

        if (input.Engulf && !player.Membrane.Type.CellWall)
        {
            player.State = Microbe.MicrobeState.Engulf;
        }
        else if (!input.Engulf && player.State == Microbe.MicrobeState.Engulf)
        {
            player.State = Microbe.MicrobeState.Normal;
        }
    }
}
