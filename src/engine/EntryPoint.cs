using Godot;

public class EntryPoint : Node
{
    public override void _Ready()
    {
        Invoke.Instance.Queue(SelectSceneToSwitch);
    }

    private void SelectSceneToSwitch()
    {
        if (LaunchOptions.ServerMode)
        {
            GD.Print("Running as server");

            // TODO: Read from server configuration file
            NetworkManager.Instance.CreateServer(new Vars());
            NetworkManager.Instance.Join();
        }
        else
        {
            GD.Print("Running as client");

            var scene = SceneManager.Instance.LoadScene("res://src/general/MainMenu.tscn");
            var mainMenu = (MainMenu)scene.Instance();
            SceneManager.Instance.SwitchToScene(mainMenu);
        }
    }
}
