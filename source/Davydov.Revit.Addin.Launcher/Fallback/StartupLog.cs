using System.IO;
using Autodesk.Revit.UI;

namespace Davydov.Revit.Addin.Launcher.Fallback;

/// <summary>
///     Writes startup failures to a file the fallback button can open. Works without the add-in's own logging,
///     which may be exactly what failed to load.
/// </summary>
internal static class StartupLog
{
    /// <summary>Gets the path of the last written log.</summary>
    public static string? FilePath { get; private set; }

    /// <summary>Appends <paramref name="text" /> to <c>%LOCALAPPDATA%\&lt;add-in&gt;\logs\&lt;Revit version&gt;\startup-error.txt</c>.</summary>
    /// <returns>The log file path.</returns>
    public static string Write(UIControlledApplication application, string text)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            PluginInfo.Name,
            "logs",
            application.ControlledApplication.VersionNumber);

        var path = Path.Combine(directory, "startup-error.txt");

        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nowhere to write; the fallback button still tells the user that startup failed
        }
        catch (UnauthorizedAccessException)
        {
        }

        FilePath = path;
        return path;
    }
}
