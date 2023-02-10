using System;
using System.Text;
using Godot;

/// <summary>
///   Unhandled exception logger for any unhandled C# exceptions
/// </summary>
public static class UnhandledExceptionLogger
{
    private static bool modsEnabled;

    public static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        var builder = new StringBuilder(500);

        // Don't change this as the launcher depends on this, in fact it would be nice to move this to a shared
        // constants file
        builder.Append("------------ Begin of Unhandled Exception Log ------------\n");
        builder.Append("The following exception ");

        if (modsEnabled)
            builder.Append("(potentially from a mod) ");

        builder.Append(args.IsTerminating ? "prevented the game from running:\n\n" : "happened:\n\n");

        builder.Append(args.ExceptionObject);

        if (modsEnabled)
        {
            builder.Append(
                "\n\nPlease provide us with this log after making sure that a loaded mod didn't cause this, " +
                "thank you.\n");
            builder.Append("If this problem was caused by a mod, please send your report instead to the mod author.\n");
        }
        else
        {
            builder.Append("\n\nPlease provide us with this log, thank you.\n");
        }

        builder.Append("------------  End of Unhandled Exception Log  ------------");

        GD.PrintErr(builder.ToString());
    }

    /// <summary>
    ///   Called by the mod loader to report when mods are loaded. This modifies the message printing to make it clear
    ///   that there are enabled mods.
    /// </summary>
    public static void ReportModsEnabled()
    {
        if (modsEnabled)
            return;

        GD.Print($"Mods reported enabled to {nameof(UnhandledExceptionLogger)}");
        modsEnabled = true;
    }
}
