using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Newtonsoft.Json;

/// <summary>
///   A networked entity the player can control.
/// </summary>
public abstract class NetworkCharacter : KinematicBody, INetworkEntity
{
    [Export]
    public bool SyncRotationX;

    [Export]
    public bool SyncRotationY;

    [Export]
    public bool SyncRotationZ;

    /// <summary>
    ///   Enables state (position, rotation) interpolation to the newly incoming state.
    ///   This adds delay, the amount of which equals to the server's tick rate.
    /// </summary>
    [Export]
    public bool EnableStateInterpolations = true;

    protected Queue<StateSnapshot> stateInterpolations = new();
    protected float lerpTimer;

    private bool setup;

    private StateSnapshot? fromState;

    private Dictionary<int, CollisionState> collisions = new();

    /// <summary>
    ///   The unique network ID self-assigned by the client. In gameplay context, this is used to differentiate
    ///   between player-character entities versus normal in-game entities.
    /// </summary>
    public int PeerId { get; set; }

    /// <summary>
    ///   Returns true if this network character owns our peer id i.e. the one we control (local player).
    /// </summary>
    public bool IsLocal => PeerId == NetworkManager.Instance.PeerId || !NetworkManager.Instance.IsMultiplayer;

    public StateSnapshot LastReceivedState { get; private set; }

    [Export]
    public float Mass { get; set; }

    [Export]
    public float Bounce { get; set; }

    [Export]
    public float LinearDamp { get; set; }

    public Vector3 LinearVelocity { get; set; }

    [JsonIgnore]
    public Spatial EntityNode => this;

    [JsonIgnore]
    public AliveMarker AliveMarker { get; } = new();

    public abstract string ResourcePath { get; }

    public uint NetworkEntityId { get; set; }

    public override void _Ready()
    {
        base._Ready();

        if (!setup && PeerId > 0)
            SetupNetworkCharacter();
    }

    public void NetworkTick(float delta)
    {
        InterpolateStates(delta);
        Simulate(delta);
    }

    public virtual void SetupNetworkCharacter()
    {
        setup = true;
    }

    public virtual void NetworkSerialize(PackedBytesBuffer buffer)
    {
        var bools = new bool[7]
        {
            !AxisLockMotionX,
            !AxisLockMotionY,
            !AxisLockMotionZ,
            SyncRotationX,
            SyncRotationY,
            SyncRotationZ,
            IsLocal,
        };
        buffer.Write(bools.ToByte());

        if (!AxisLockMotionX)
        {
            if (IsLocal)
                buffer.Write(LinearVelocity.x);

            buffer.Write(GlobalTranslation.x);
        }

        if (!AxisLockMotionY)
        {
            if (IsLocal)
                buffer.Write(LinearVelocity.y);

            buffer.Write(GlobalTranslation.y);
        }

        if (!AxisLockMotionZ)
        {
            if (IsLocal)
                buffer.Write(LinearVelocity.z);

            buffer.Write(GlobalTranslation.z);
        }

        if (SyncRotationX)
            buffer.Write(GlobalRotation.x);

        if (SyncRotationY)
            buffer.Write(GlobalRotation.y);

        if (SyncRotationZ)
            buffer.Write(GlobalRotation.z);
    }

    public StateSnapshot DecodePacket(PackedBytesBuffer buffer)
    {
        buffer.Position = 0;

        var bools = buffer.ReadByte();

        float xPos, yPos, zPos, xRot, yRot, zRot, xVel, yVel, zVel;
        xPos = yPos = zPos = xRot = yRot = zRot = xVel = yVel = zVel = 0;

        if (bools.ToBoolean(0))
        {
            if (bools.ToBoolean(6))
                xVel = buffer.ReadSingle();

            xPos = buffer.ReadSingle();
        }

        if (bools.ToBoolean(1))
        {
            if (bools.ToBoolean(6))
                yVel = buffer.ReadSingle();

            yPos = buffer.ReadSingle();
        }

        if (bools.ToBoolean(2))
        {
            if (bools.ToBoolean(6))
                zVel = buffer.ReadSingle();

            zPos = buffer.ReadSingle();
        }

        if (bools.ToBoolean(3))
            xRot = buffer.ReadSingle();

        if (bools.ToBoolean(4))
            yRot = buffer.ReadSingle();

        if (bools.ToBoolean(5))
            zRot = buffer.ReadSingle();

        return new StateSnapshot
        {
            LinearVelocity = new Vector3(xVel, yVel, zVel),
            Position = new Vector3(xPos, yPos, zPos),
            Rotation = new Quat(new Vector3(xRot, yRot, zRot)),
        };
    }

    public virtual void NetworkDeserialize(PackedBytesBuffer buffer)
    {
        LastReceivedState = DecodePacket(buffer);

        if (IsLocal)
        {
            // Using CSP and reconciliation
            return;
        }

        ApplyState(LastReceivedState, EnableStateInterpolations);
    }

    public virtual void PackSpawnState(PackedBytesBuffer buffer)
    {
        buffer.Write(PeerId);
    }

    public virtual void OnRemoteSpawn(PackedBytesBuffer buffer, GameProperties currentGame)
    {
        PeerId = buffer.ReadInt32();
    }

    /// <summary>
    ///   Applies a networked input to this character.
    /// </summary>
    public abstract void ApplyNetworkedInput(NetworkInputVars input);

    public void ApplyState(StateSnapshot state, bool interpolate)
    {
        LinearVelocity = state.LinearVelocity;

        if (interpolate)
        {
            while (stateInterpolations.Count > 2)
                stateInterpolations.Dequeue();

            stateInterpolations.Enqueue(state);
        }
        else
        {
            GlobalTransform = new Transform(state.Rotation, state.Position);
        }
    }

    /// <summary>
    ///   Records the current rigidbody state into a snapshot.
    /// </summary>
    public virtual StateSnapshot ToSnapshot()
    {
        return new StateSnapshot
        {
            LinearVelocity = LinearVelocity,
            Position = GlobalTranslation,
            Rotation = GlobalTransform.basis.Quat(),
        };
    }

    public virtual void OnDestroyed()
    {
        AliveMarker.Alive = false;
    }

    protected abstract void IntegrateForces(float delta);

    protected abstract void OnContactBegin(Node body, int bodyShape, int localShape);

    protected abstract void OnContactEnd(Node body, int bodyShape, int localShape);

    private void Simulate(float delta)
    {
        // Damping
        LinearVelocity *= 1.0f - delta * LinearDamp;

        IntegrateForces(delta);

        var collision = MoveAndCollide(LinearVelocity * delta, false);

        foreach (var entry in collisions)
        {
            foreach (var shapePair in entry.Value.Shapes)
            {
                // Untag potentially no longer colliding shapes
                shapePair.Tagged = false;
            }
        }

        if (collision != null)
        {
            if (collision.Collider is not CollisionObject otherBody)
                return;

            // Resolve collision with other bodies
            // TODO: tweak and make these closer to original Rigidbody's behaviour

            if (otherBody is NetworkCharacter otherCharacterBody)
            {
                otherCharacterBody.LinearVelocity += collision.Remainder / delta;
            }
            else if (otherBody is RigidBody otherRigidbody)
            {
                otherRigidbody.ApplyImpulse(collision.Position, collision.Remainder);
            }

            LinearVelocity -= collision.Remainder / delta;

            var localShapeIndex = -1;

            // Get index of the local shape
            foreach (int owner in GetShapeOwners())
            {
                for (int i = 0; i < ShapeOwnerGetShapeCount((uint)owner); ++i)
                {
                    // There's ColliderShapeIndex but no LocalShapeIndex, why? We may never know...
                    // In the meantime, just use this workaround
                    if (collision.LocalShape == ShapeOwnerGetShape((uint)owner, i))
                    {
                        localShapeIndex = ShapeOwnerGetShapeIndex((uint)owner, i);
                        break;
                    }

                    if (localShapeIndex != -1)
                        break;
                }
            }

            var colliderId = collision.ColliderRid.GetId();

            var shape = new ShapePair(collision.ColliderShapeIndex, localShapeIndex);

            if (!collisions.ContainsKey(colliderId))
                collisions[colliderId] = new CollisionState(otherBody);

            if (collisions[colliderId].Shapes.TryGetValue(shape, out ShapePair cachedShape))
            {
                // Still colliding
                cachedShape.Tagged = true;
            }
            else
            {
                // Begins colliding
                shape.Tagged = true;
                collisions[colliderId].Shapes.Add(shape);
                OnContactBegin(otherBody, collision.ColliderShapeIndex, localShapeIndex);
            }
        }

        foreach (var entry in collisions.ToList())
        {
            var shapes = entry.Value.Shapes;

            foreach (var shape in shapes.ToHashSet())
            {
                // Not colliding in the earlier step, safe to assume this shape is indeed has stopped colliding
                if (!shape.Tagged)
                {
                    shapes.Remove(shape);
                    OnContactEnd(entry.Value.Body, shape.BodyShape, shape.LocalShape);
                }
            }

            if (shapes.Count <= 0)
                collisions.Remove(entry.Key);
        }
    }

    private void InterpolateStates(float delta)
    {
        if (!NetworkManager.Instance.IsClient)
            return;

        lerpTimer += delta;

        var tickInterval = 1f / NetworkManager.Instance.ServerSettings.GetVar<int>("TickRate");

        if (lerpTimer > tickInterval)
        {
            lerpTimer = 0;

            if (stateInterpolations.Count > (IsLocal ? 0 : 1))
                fromState = stateInterpolations.Dequeue();
        }

        if (stateInterpolations.Count <= 0 || !fromState.HasValue)
            return;

        var toState = stateInterpolations.Peek();

        var weight = lerpTimer / tickInterval;

        var position = fromState.Value.Position.LinearInterpolate(toState.Position, weight);
        var rotation = fromState.Value.Rotation.Slerp(toState.Rotation, weight);

        GlobalTransform = new Transform(rotation, position);
    }

    public struct StateSnapshot
    {
        public Vector3 LinearVelocity { get; set; }
        public Vector3 Position { get; set; }
        public Quat Rotation { get; set; }
    }

    private class CollisionState
    {
        public CollisionState(Node body)
        {
            Body = body;
        }

        public Node Body { get; set; }
        public HashSet<ShapePair> Shapes { get; set; } = new();
    }

    private class ShapePair : IEquatable<ShapePair>
    {
        public ShapePair(int bodyShape, int localShape)
        {
            BodyShape = bodyShape;
            LocalShape = localShape;
        }

        public int BodyShape { get; }
        public int LocalShape { get; }
        public bool Tagged { get; set; }

        public bool Equals(ShapePair other)
        {
            return BodyShape == other.BodyShape && LocalShape == other.LocalShape;
        }

        public override bool Equals(object obj)
        {
            if (obj is ShapePair shape)
            {
                return Equals(shape);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return BodyShape ^ LocalShape;
        }
    }
}
