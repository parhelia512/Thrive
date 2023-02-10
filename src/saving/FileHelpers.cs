using System.IO;
using Godot;
using FileAccess = Godot.FileAccess;

/// <summary>
///   Helpers regarding file operations
/// </summary>
public static class FileHelpers
{
    /// <summary>
    ///   Makes sure the save directory exists
    /// </summary>
    public static void MakeSureDirectoryExists(string path)
    {
        using var directory = DirAccess.Open("user://");
        var result = directory.MakeDirRecursive(path);

        if (result != Error.AlreadyExists && result != Error.Ok)
        {
            throw new IOException($"can't create folder: {path}");
        }
    }

    /// <summary>
    ///   Attempts to delete a file
    /// </summary>
    /// <param name="path">Path to the file</param>
    /// <returns>True on success</returns>
    public static bool DeleteFile(string path)
    {
        var result = DirAccess.RemoveAbsolute(path);
        return result == Error.Ok;
    }

    /// <summary>
    ///   Returns true if file exists
    /// </summary>
    /// <param name="path">Path to check</param>
    /// <returns>True if exists, false otherwise</returns>
    public static bool Exists(string path)
    {
        return FileAccess.FileExists(path);
    }

    /// <summary>
    ///   Tests if it is possible to create/write a file at the given path
    /// </summary>
    /// <param name="path">Path to file</param>
    /// <returns>File write access status, <see cref="Error.Ok"/> if successful</returns>
    /// <remarks>
    ///   <para>
    ///     THIS FUNCTION IS DESTRUCTIVE! MAKE SURE THE FILE TO TEST DOESN'T EXIST!
    ///   </para>
    /// </remarks>
    public static Error TryWriteFile(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file?.GetError() != Error.Ok)
            return file?.GetError() ?? Error.Failed;

        DeleteFile(path);

        return Error.Ok;
    }
}
