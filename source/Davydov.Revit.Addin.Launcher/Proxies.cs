using Autodesk.Revit.Attributes;

namespace Davydov.Revit.Addin.Launcher.Proxies;

// One line per ribbon button and per availability class (plan, section 8).
// The proxy name must match the add-in class name; the add-in checks this at startup.

[Transaction(TransactionMode.Manual)] public sealed class StatusCommand : ProxyCommand;
[Transaction(TransactionMode.Manual)] public sealed class ActiveDocumentCommand : ProxyCommand;

public sealed class DocumentAvailability : ProxyAvailability;
public sealed class ProjectAvailability : ProxyAvailability;
