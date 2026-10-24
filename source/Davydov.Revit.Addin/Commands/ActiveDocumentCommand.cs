#if DEBUG
using Autodesk.Revit.Attributes;
using Davydov.Revit.Addin.Features.Dev;
using Davydov.Revit.Hosting.Commands;
using Nice3point.Revit.Toolkit.External;

namespace Davydov.Revit.Addin.Commands;

/// <summary>
///     Debug-only button: exercises an availability class through the Launcher proxy (enabled only while a document is
///     open) and the whole pipeline — <c>ICommandSender</c>, middleware, <c>ActiveDocument</c> — with <see cref="CheckActiveDocument" />.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class ActiveDocumentCommand : ExternalCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        Host.GetService<ICommandSender>().Send<CheckActiveDocument>();
    }
}
#endif
