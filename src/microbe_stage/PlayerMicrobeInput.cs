using System;
using Godot;

/// <summary>
///   Handles key input in the microbe stage
/// </summary>
/// <remarks>
///   <para>
///     Note that callbacks from other places directly call some methods in this class, so
///     an extra care should be taken while modifying the methods as otherwise some stuff
///     may no longer work.
///   </para>
/// </remarks>
public class PlayerMicrobeInput : PlayerInputBase
{
    protected MicrobeStage Stage => stage as MicrobeStage ??
        throw new InvalidOperationException("Stage hasn't been set");

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

        if (Stage.Player != null)
        {
            if (Stage.Player.State == Microbe.MicrobeState.Unbinding)
            {
                Stage.Player.MovementDirection = Vector3.Zero;
                return;
            }

            var movement = new Vector3(leftRightMovement, 0, forwardMovement);

            // TODO: change this line to only normalize when length exceeds 1 to make slowly moving with a controller
            // work
            var direction = autoMove ? new Vector3(0, 0, -1) : movement.Normalized();

            Stage.Player.MovementDirection = direction;
            Stage.Player.LookAtPoint = Stage.Camera.CursorWorldPos;
        }
    }

    [RunOnKeyDown("g_fire_toxin")]
    public void EmitToxin()
    {
        Stage.Player?.EmitToxin();
    }

    [RunOnKey("g_secrete_slime")]
    public void SecreteSlime(float delta)
    {
        Stage.Player?.QueueSecreteSlime(delta);
    }

    [RunOnKeyDown("g_toggle_engulf")]
    public void ToggleEngulf()
    {
        if (Stage.Player == null)
            return;

        if (Stage.Player.State == Microbe.MicrobeState.Engulf)
        {
            Stage.Player.State = Microbe.MicrobeState.Normal;
        }
        else if (!Stage.Player.Membrane.Type.CellWall)
        {
            Stage.Player.State = Microbe.MicrobeState.Engulf;
        }
    }

    [RunOnKeyDown("g_toggle_binding")]
    public void ToggleBinding()
    {
        if (Stage.Player == null)
            return;

        if (Stage.Player.State == Microbe.MicrobeState.Binding)
        {
            Stage.Player.State = Microbe.MicrobeState.Normal;
        }
        else if (Stage.Player.CanBind)
        {
            Stage.Player.State = Microbe.MicrobeState.Binding;
        }
    }

    [RunOnKeyDown("g_toggle_unbinding")]
    public void ToggleUnbinding()
    {
        if (Stage.Player == null)
            return;

        if (Stage.Player.State == Microbe.MicrobeState.Unbinding)
        {
            Stage.HUD.HintText = string.Empty;
            Stage.Player.State = Microbe.MicrobeState.Normal;
        }
        else if (Stage.Player.Colony != null && !Stage.Player.IsMulticellular)
        {
            Stage.HUD.HintText = TranslationServer.Translate("UNBIND_HELP_TEXT");
            Stage.Player.State = Microbe.MicrobeState.Unbinding;
        }
    }

    [RunOnKeyDown("g_unbind_all")]
    public void UnbindAll()
    {
        Stage.Player?.UnbindAll();
    }

    [RunOnKeyDown("g_perform_unbinding", Priority = 1)]
    public bool AcceptUnbind()
    {
        if (Stage.Player?.State != Microbe.MicrobeState.Unbinding)
            return false;

        if (Stage.HoverInfo.HoveredMicrobes.Count == 0)
            return false;

        var target = Stage.HoverInfo.HoveredMicrobes[0];
        RemoveCellFromColony(target);

        Stage.HUD.HintText = string.Empty;
        return true;
    }

    [RunOnKeyDown("g_pack_commands")]
    public bool ShowSignalingCommandsMenu()
    {
        if (Stage.Player?.HasSignalingAgent != true)
            return false;

        Stage.HUD.ShowSignalingCommandsMenu(Stage.Player);

        // We need to not consume the input, otherwise the key up for this will not run
        return false;
    }

    [RunOnKeyUp("g_pack_commands")]
    public void CloseSignalingCommandsMenu()
    {
        var command = Stage.HUD.SelectSignalCommandIfOpen();

        if (Stage.Player != null)
            Stage.HUD.ApplySignalCommand(command, Stage.Player);
    }

    [RunOnKeyDown("g_cheat_editor")]
    public void CheatEditor()
    {
        if (Settings.Instance.CheatsEnabled)
        {
            Stage.HUD.ShowReproductionDialog();
        }
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

    private void RemoveCellFromColony(Microbe target)
    {
        if (target.Colony == null)
        {
            GD.PrintErr("Target microbe is not a part of colony");
            return;
        }

        target.Colony.RemoveFromColony(target);
    }

    private void SpawnCheatCloud(string name, float delta)
    {
        float multiplier = 1.0f;

        // To make cheating easier in multicellular with large cell layouts
        if (Stage.Player?.IsMulticellular == true)
            multiplier = 4;

        Stage.Clouds.AddCloud(SimulationParameters.Instance.GetCompound(name),
            Constants.CLOUD_CHEAT_DENSITY * delta * multiplier, Stage.Camera.CursorWorldPos);
    }
}
