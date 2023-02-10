using Godot;

public interface IPhotographable
{
    string SceneToPhotographPath { get; }

    void ApplySceneParameters(Node3D instancedScene);
    float CalculatePhotographDistance(Node3D instancedScene);
}
