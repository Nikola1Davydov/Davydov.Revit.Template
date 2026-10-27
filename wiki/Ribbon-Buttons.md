# Ribbon buttons

A button has two parts: the class in the add-in and one proxy line in the Launcher. Revit creates a button's class from
the button's assembly, and the add-in lives in the Launcher's isolated context — so the button points to a same-named
proxy in the Launcher, and the proxy forwards the click.

## 1. The button class, in `Commands/`

```csharp
using Autodesk.Revit.Attributes;
using Acme.Revit.Tools.Hosting.Commands;
using Nice3point.Revit.Toolkit.External;

namespace Acme.Revit.Tools.Commands;

[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class RenameFamiliesCommand : ExternalCommand
{
    public override void Execute()
    {
        Host.GetService<ICommandSender>().Send<RenameFamilies>();
    }
}
```

* **Keep it thin:** send a [command](Commands) or open a [page](Pages) (`Host.GetService<IWindows>().Open("/route")`).
* `Execute` runs on the Revit thread, so a short direct use of the Revit API is fine here, and so is `ActiveUIDocument`:
  the click belongs to the active document.
* Revit creates the class, so no constructor dependencies: use `Host.GetService<T>()`.
* `ICommandSender.Send` does not wait. It cannot: the command reaches the Revit thread through an external event that runs
  only after the button returns. A failure is shown to the user in a task dialog.

## 2. The proxy, in the Launcher

One line in `source/Acme.Revit.Tools.Launcher/Proxies.cs`, with the same class name:

```csharp
[Transaction(TransactionMode.Manual)] public sealed class RenameFamiliesCommand : ProxyCommand;
```

Forget it, and the add-in fails at startup with «The Launcher has no proxy for '…'. Add this line to Proxies.cs …».
Two button classes with the same name in different namespaces cannot both have a proxy — keep button names unique.

## 3. The button on the ribbon

In `Application.CreateRibbon` (`source/Acme.Revit.Tools/Application.cs`):

```csharp
var panel = Application.CreatePanel("Families", "Acme.Revit.Tools");

panel.AddButton<RenameFamiliesCommand>("Rename\nfamilies")
    .SetAvailability<ProjectAvailability>()
    .WithIcons("RenameFamilies");
```

* `AddButton<T>` and `SetAvailability<T>` come from `Infrastructure/Ribbon`. Never `AddPushButton<T>` of
  Nice3point.Revit.Extensions: it points the button to the add-in assembly and bypasses the isolation.
* `\n` breaks the label into two lines.

## Availability

* `DocumentAvailability` — enabled while a document is open; `ProjectAvailability` — while a project (not a family) is
  open. Both already have proxies.
* Your own rule: a class implementing `IExternalCommandAvailability` in the add-in, plus a proxy line:

  ```csharp
  public sealed class SelectionAvailability : ProxyAvailability;
  ```

Revit asks availability very often — keep the check trivial. An exception in it counts as «unavailable».

## Icons

Two PNG files in `source/Acme.Revit.Tools/Resources/Icons/`: `<Name>16.png` (16×16) and `<Name>32.png` (32×32).
`WithIcons("<Name>")` sets both. They are embedded resources, not WPF resources: a WPF `pack://` URI resolves the
assembly by name and could load a second copy of the add-in outside the isolated context.

## Debug-only buttons

Wrap the button class and its `AddButton` call in `#if DEBUG`; keep the proxy line without `#if` — a proxy without a
button does no harm. The «Dev» panel of the template is built this way.

## Never rename a released button

The class name and namespace are the button's internal name. Revit binds keyboard shortcuts and the Quick Access Toolbar
to it; renaming or moving the class loses the user's settings.
