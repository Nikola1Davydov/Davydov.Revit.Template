using Autodesk.Revit.Attributes;
using Davydov.Revit.Hosting.WebView.Controls;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Opens the sample page in a window; a second click brings the open window to front.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class SamplePageCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<IWindows>().Open("/", options =>
        {
            options.Width = 900;
            options.Height = 640;
        });
    }
}
