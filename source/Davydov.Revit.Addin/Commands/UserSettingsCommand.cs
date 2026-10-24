#if DEBUG
using Autodesk.Revit.Attributes;
using Davydov.Revit.Addin.Diagnostics;
using Davydov.Revit.Hosting.Notifications;
using Davydov.Revit.Hosting.Settings;
using Microsoft.Extensions.Options;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Debug-only button that writes a user setting and shows the value <c>IOptionsMonitor</c> currently sees.
///     Click a few times, restart Revit, click again: the counter continues.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class UserSettingsCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        var settings = Host.GetService<IUserSettings>();
        var options = Host.GetService<IOptionsMonitor<DevOptions>>();
        var notifier = Host.GetService<IUserNotifier>();

        // A button must not block on async work: the write finishes in the background, the dialog comes after it
        _ = Task.Run(async () =>
        {
            await settings.UpdateAsync<DevOptions>(DevOptions.SectionName, dev => dev.ClickCount++);
            await Task.Delay(TimeSpan.FromMilliseconds(500));   // the file watcher reloads the configuration

            notifier.Info("Davydov.Revit.Addin", $"Clicks: {options.CurrentValue.ClickCount}\nFile: {settings.FilePath}");
        });
    }
}
#endif
