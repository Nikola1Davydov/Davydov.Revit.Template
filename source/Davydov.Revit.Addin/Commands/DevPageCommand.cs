#if DEBUG
using Autodesk.Revit.Attributes;
using Davydov.Revit.Hosting.WebView.Controls;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Debug-only button: shows the «Dev» page in the dock pane — the live log and a form that runs any registered command.
///     The pane is one per add-in, so the page takes the place of the palette until «Palette» is pressed again.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class DevPageCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<IWindows>().ShowDockPane("/dev");
    }
}
#endif
