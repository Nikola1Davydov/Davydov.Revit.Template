using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;

namespace Davydov.Revit.Addin.Launcher.Fallback;

/// <summary>
///     Shows a single «startup error» button on the Add-Ins tab when the add-in failed to start, instead of an empty
///     ribbon, which would leave the user without a hint. Needs nothing but the Launcher itself: the add-in is what failed.
/// </summary>
internal static class FallbackRibbon
{
    public static void Create(UIControlledApplication application, string logPath)
    {
        try
        {
            var panel = application.CreateRibbonPanel($"{PluginInfo.Name}: startup error");
            var buttonData = new PushButtonData(
                $"{PluginInfo.Name}.Launcher.StartupError",
                "Startup\nerror",
                typeof(FallbackRibbon).Assembly.Location,
                typeof(OpenStartupLogCommand).FullName)
            {
                ToolTip = $"{PluginInfo.Name} failed to start. Click to open the log:{Environment.NewLine}{logPath}"
            };

            panel.AddItem(buttonData);
        }
        catch
        {
            // The ribbon may be unavailable; the log file is still written
        }
    }
}

/// <summary>Opens the startup error log.</summary>
[Transaction(TransactionMode.Manual)]
public sealed class OpenStartupLogCommand : IExternalCommand
{
    /// <inheritdoc />
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var path = StartupLog.FilePath;
        if (path is null || !System.IO.File.Exists(path))
        {
            TaskDialog.Show(PluginInfo.Name, "The startup log was not written.");
            return Result.Succeeded;
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        return Result.Succeeded;
    }
}
