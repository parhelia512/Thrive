using System;
using System.Linq;
using Godot;

/// <summary>
///   Provides access to Godot launch options
/// </summary>
public static class LaunchOptions
{
    private static readonly Lazy<string[]> GodotLaunchOptions = new(OS.GetCmdlineArgs);
    private static readonly Lazy<bool> DisableVideosOption = new(ReadDisableVideo);
    private static readonly Lazy<bool> RunAsServer = new(ReadRunAsServer);

    public static bool VideosEnabled => !DisableVideosOption.Value;

    public static bool ServerMode => RunAsServer.Value;

    private static bool ReadDisableVideo()
    {
        bool value = GodotLaunchOptions.Value.Any(o => o == Constants.DISABLE_VIDEOS_LAUNCH_OPTION);

        if (value)
            GD.Print("Videos are disabled with a command line option");

        return value;
    }

    private static bool ReadRunAsServer()
    {
        bool value = GodotLaunchOptions.Value.Any(o => o == Constants.RUN_AS_SERVER_LAUNCH_OPTION);

        return value;
    }
}
