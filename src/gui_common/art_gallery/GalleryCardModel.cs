using Godot;

public partial class GalleryCardModel : GalleryCard
{
    private ImageTask? imageTask;

#pragma warning disable CA2213
    private Texture2D imageLoadingIcon = null!;
#pragma warning restore CA2213

    private bool finishedLoadingImage;

    public override void _Ready()
    {
        base._Ready();

        imageLoadingIcon = GD.Load<Texture2D>("res://assets/textures/gui/bevel/IconGenerating.png");
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        if (finishedLoadingImage)
            return;

        if (imageTask != null)
        {
            if (imageTask.Finished)
            {
                Thumbnail = imageTask.FinalImage;
                finishedLoadingImage = true;
            }

            return;
        }

        imageTask = new ImageTask(new ModelPreview(Asset.ResourcePath, Asset.MeshNodePath!));

        PhotoStudio.Instance.SubmitTask(imageTask);

        Thumbnail = imageLoadingIcon;
    }

    public class ModelPreview : IPhotographable
    {
        private string resourcePath;
        private string meshNodePath;

        public ModelPreview(string resourcePath, string meshNodePath)
        {
            this.resourcePath = resourcePath;
            this.meshNodePath = meshNodePath;
        }

        public string SceneToPhotographPath => resourcePath;

        public void ApplySceneParameters(Node3D instancedScene)
        {
        }

        public float CalculatePhotographDistance(Node3D instancedScene)
        {
            var instancedMesh = instancedScene.GetNode<MeshInstance3D>(meshNodePath);
            return instancedMesh.GetAabb().Size.Length(); // TODO: ?
        }
    }
}
