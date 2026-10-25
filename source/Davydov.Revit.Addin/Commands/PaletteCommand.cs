using Autodesk.Revit.Attributes;
using Davydov.Revit.Hosting.WebView.Controls;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Shows the sample page in the dock pane of the add-in.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class PaletteCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<IWindows>().ShowDockPane("/dock");
    }
}
