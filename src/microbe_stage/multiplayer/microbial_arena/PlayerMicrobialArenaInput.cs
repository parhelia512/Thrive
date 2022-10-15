using Godot;
using System;

public class PlayerMicrobialArenaInput : PlayerInputBase<MicrobialArena>
{
    private bool wasMoving;

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

            if (!IsNetworkMaster())
            {
                if (!direction.IsEqualApprox(Vector3.Zero))
                {
                    stage.Player.SendMovementDirection(direction);
                    wasMoving = true;
                }
                else if (direction.IsEqualApprox(Vector3.Zero) && wasMoving)
                {
                    stage.Player.SendMovementDirection(direction);
                    wasMoving = false;
                }

                // TODO: make this not be sent every frame
                stage.Player.SendLookAtPoint(stage.Camera.CursorWorldPos);
            }
        }
    }

    [RunOnKeyDown("g_toggle_engulf")]
    public void ToggleEngulf()
    {
        if (stage!.Player == null)
            return;

        if (stage.Player.State == Microbe.MicrobeState.Engulf)
        {
            stage.Player.State = Microbe.MicrobeState.Normal;

            if (!IsNetworkMaster())
                stage.Player.SendEngulfMode(false);
        }
        else if (!stage.Player.Membrane.Type.CellWall)
        {
            stage.Player.State = Microbe.MicrobeState.Engulf;

            if (!IsNetworkMaster())
                stage.Player.SendEngulfMode(true);
        }
    }

    [RunOnKeyChange("g_toggle_scoreboard")]
    public void ShowScoreBoard(bool heldDown)
    {
        stage?.HUD.ToggleScoreBoard();
    }
}
