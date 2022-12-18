using System;
using Godot;

public class PlayerMicrobialArenaInput : PlayerInputBase<MicrobialArena>
{
    private Random random = new();

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

            stage.Player.MovementDirection = direction;
            stage.Player.LookAtPoint = stage.Camera.CursorWorldPos;
        }
    }

    [RunOnKeyDown("g_fire_toxin")]
    public void EmitToxin()
    {
        stage!.Player?.QueueEmitToxin(SimulationParameters.Instance.GetCompound("oxytoxy"));
    }

    [RunOnKey("g_secrete_slime")]
    public void SecreteSlime(float delta)
    {
        stage!.Player?.QueueSecreteSlime(delta);
    }

    [RunOnKeyDown("g_toggle_engulf")]
    public void ToggleEngulf()
    {
        if (stage!.Player == null)
            return;

        stage.Player.WantsToEngulf = !stage.Player.WantsToEngulf;
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

    private void SpawnCheatCloud(string name, float delta)
    {
        SpawnHelpers.SpawnCloud(stage!.Clouds, stage.Camera.CursorWorldPos,
            SimulationParameters.Instance.GetCompound(name), Constants.CLOUD_CHEAT_DENSITY * delta, random);
    }
}
