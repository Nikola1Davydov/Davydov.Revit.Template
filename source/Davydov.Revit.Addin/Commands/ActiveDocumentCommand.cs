#if DEBUG
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Microsoft.Extensions.Logging;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Debug-only button that exercises an availability class through the Launcher proxy:
///     it is enabled only while a document is open.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public partial class ActiveDocumentCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        var logger = Host.GetService<ILogger<ActiveDocumentCommand>>();
        var title = Application.ActiveUIDocument?.Document.Title ?? "<none>";   // a click belongs to the active document (plan, section 7)

        LogActiveDocument(logger, title);
        TaskDialog.Show("Davydov.Revit.Addin", $"Active document: {title}");
    }

    [LoggerMessage(LogLevel.Information, "Active document: {Title}")]
    private static partial void LogActiveDocument(ILogger logger, string title);
}
#endif
