using Davydov.Revit.Addin.Infrastructure.Ribbon;
using Davydov.Revit.Addin.Commands;
using Davydov.Revit.Addin.Resources;
using Nice3point.Revit.Extensions.UI;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin;

/// <summary>
///     Provides the Revit application entry point.
/// </summary>
/// <remarks>
///     Revit loads Davydov.Revit.Addin.Launcher, which creates this class inside its isolated AssemblyLoadContext.
///     An exception here reaches the Launcher: it writes the startup log and lets Revit report the failure.
/// </remarks>
[UsedImplicitly]
public class Application : AsyncExternalApplication
{
    /// <inheritdoc />
    public override async Task OnStartupAsync()
    {
        await Host.StartAsync(Application);
        CreateRibbon();
    }

    /// <inheritdoc />
    public override async Task OnShutdownAsync()
    {
        await Host.StopAsync();
    }

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("General", "Davydov.Revit.Addin");

        panel.AddButton<StatusCommand>("Status")
            .WithIcons("RibbonIcon");

#if DEBUG
        var devPanel = Application.CreatePanel("Dev", "Davydov.Revit.Addin");

        devPanel.AddButton<ActiveDocumentCommand>("Active\ndocument")
            .SetAvailability<DocumentAvailability>()
            .WithIcons("RibbonIcon");

        devPanel.AddButton<UserSettingsCommand>("User\nsettings")
            .WithIcons("RibbonIcon");

        devPanel.AddButton<DevPageCommand>("Dev\npage")
            .WithIcons("RibbonIcon");
#endif
    }
}
