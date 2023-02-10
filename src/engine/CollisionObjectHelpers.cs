using Godot;

/// <summary>
///   Common helper operations for CollisionObjects
/// </summary>
public static class CollisionObjectHelpers
{
    /// <summary>
    ///  Creates and returns a new shape owner for a shape and the given entity.
    ///  Applies a given transform to the new shapeOwner.
    /// </summary>
    /// <returns>Returns the id of the newly created shapeOwner</returns>
    public static uint CreateShapeOwnerWithTransform(this CollisionObject3D entity, Transform3D transform, Shape3D shape)
    {
        var newShapeOwnerId = entity.CreateShapeOwner(shape);
        entity.ShapeOwnerAddShape(newShapeOwnerId, shape);
        entity.ShapeOwnerSetTransform(newShapeOwnerId, transform);
        return newShapeOwnerId;
    }

    /// <summary>
    ///  Creates a new shapeOwner for the first shape in the oldShapeOwner of the oldParent
    ///  and applies a transform to it.
    ///  Doesn't destroy the oldShapeOwner.
    /// </summary>
    /// <returns>Returns the new ShapeOwnerId.</returns>
    public static uint CreateNewOwnerId(this CollisionObject3D oldParent,
        CollisionObject3D newParent, Transform3D transform, uint oldShapeOwnerId)
    {
        var shape = oldParent.ShapeOwnerGetShape(oldShapeOwnerId, 0);
        var newShapeOwnerId = CreateShapeOwnerWithTransform(newParent, transform, shape);
        return newShapeOwnerId;
    }
}
