# Commands

A command is the code that reads or changes the Revit model. It is a class in `Features/<Slice>/`, like a controller of an
ASP.NET Core app: a request in, a response out. Ribbon buttons, React pages and AI clients all call commands; nothing
else touches the Revit API.

## A command without input

```csharp
using Acme.Revit.Tools.Services.Documents;
using Acme.Revit.Tools.Hosting.Commands;

namespace Acme.Revit.Tools.Features.Families;

[Command(Description = "Returns the name and category of every family in the active document.")]
public sealed class GetFamilies(ActiveDocument document) : ICommand<IReadOnlyList<FamilyItem>>
{
    public Task<IReadOnlyList<FamilyItem>> ExecuteAsync(CommandContext context)
    {
        return document.InvokeAsync<IReadOnlyList<FamilyItem>>(
            revitDocument => new FilteredElementCollector(revitDocument)
                .OfClass(typeof(Family))
                .Cast<Family>()
                .Select(family => new FamilyItem(family.Id.Value, family.Name, family.FamilyCategory?.Name))
                .ToList(),
            context.CancellationToken);
    }
}

public sealed record FamilyItem(long Id, string Name, string? Category);
```

## A command with input

```csharp
public sealed record RenameFamiliesRequest(IReadOnlyList<long> Ids, string Prefix);

public sealed class RenameFamilies(ActiveDocument document) : ICommand<RenameFamiliesRequest, Empty>
{
    public async Task<Empty> ExecuteAsync(RenameFamiliesRequest request, CommandContext context)
    {
        await document.InvokeAsync(revitDocument =>
        {
            using var transaction = new Transaction(revitDocument, "Rename families");
            transaction.Start();
            // ...
            transaction.Commit();
        }, context.CancellationToken);

        return Empty.Value;
    }
}
```

## The rules

* **Registered by reflection.** No registration code: the registry finds every command class at startup and reports
  all problems at once — a duplicate name, a request it cannot create.
* **The name** is the class name without a `Command` suffix (`GetFamilies`), or `[Command("Name")]`. Pages and AI clients
  call it by this name.
* **Request and response are records.** JSON is camelCase both ways. `Empty` is the response of a command without a
  result. A request whose parameters all have defaults may come without a payload; with a required parameter, a call
  without a payload is `bad_request`.
* **`[Command(Description = ...)]`** says what the command does — for people, the Dev page and AI clients.
* **Dependencies through the constructor.** A command is scoped to one call.
* **Return plain data** — records, ids, strings — never Revit objects: they are valid only on the Revit thread.

## The Revit thread and `ActiveDocument`

A command runs on the thread pool. The Revit API works only on the Revit thread, so the command reaches the model through
`ActiveDocument`:

* `InvokeAsync(document => ...)` — work on the Revit thread with the document of this call; transactions are allowed.
* `InvokeUIAsync(uiDocument => ...)` — the same with the `UIDocument`, for selection and views.
* Never use the Revit API outside these lambdas.
* Never `ActiveUIDocument` in `Features/` and `Services/`. The first access fixes the document of the call; if the user
  switches documents before a later access, it fails with `document_changed` instead of touching another model. The build
  rejects `ActiveUIDocument` there.

`IRevitDispatcher.InvokeAsync(application => ...)` is the lower level: the Revit thread without a document rule, for work
that needs no document.

## Long work: batches, progress, cancellation

```csharp
await document.InvokeBatchedAsync(ids, 500, (revitDocument, batch) =>
{
    // one Revit call per batch
}, context.Progress, context.CancellationToken);
```

Between batches Revit gets control back, so it stays responsive, the page shows progress («N / M») and the user can
cancel. Changing the model in batches means one transaction — one undo step — per batch. Report progress yourself with
`context.Progress.Report(new ProgressInfo(fraction, message))`, and pass `context.CancellationToken` everywhere.

## Errors

```csharp
throw new CommandException("project_only", "Open a project, not a family.");
```

A `CommandException` reaches the caller as its code and message: a page shows the message, a button shows it in a task
dialog, an AI client gets a tool error. Any other exception becomes `failed` and is logged as an error.

Codes callers already know: `CommandErrorCodes` — `not_found`, `bad_request`, `already_running`, `canceled`, `failed`;
`DocumentErrorCodes` — `no_document`, `document_changed`.

## The pipeline

Every call goes `ExceptionHandling → CommandLogging → DocumentCheckMiddleware → command`, whoever calls:

* **CommandLogging** writes `Command GetFamilies (webview2) succeeded in 12 ms` — the source is `webview2`, `ribbon` or
  `mcp`.
* **DocumentCheckMiddleware** reads the `Document` header a page sends and fixes the document of the call up front, so
  data a page loaded from one document is never applied to another.

Your own middleware: a class implementing `ICommandMiddleware`, added with `.Use<T>()` in `AddRevitCommands` in
`Host.cs`.

## Calling a command

| From | How |
| --- | --- |
| A React page | `useCommand<FamilyItem[]>("GetFamilies")`, `useCommandMutation<void, RenameFamiliesRequest>("RenameFamilies")` — see [Pages](Pages) |
| A ribbon button | `Host.GetService<ICommandSender>().Send<RenameFamilies>(request)` — fire and forget, failures shown to the user — see [Ribbon buttons](Ribbon-Buttons) |
| C# code that needs the result | `ICommandExecutor.ExecuteAsync(new CommandRequest(name, payload, source))` — the payload as a `JsonElement` — returns a `CommandResult` |
| An AI client | mark the class with `[McpTool]` — see [AI and MCP](AI-and-MCP) |
| By hand, in Debug | the «Commands» tab of the Dev page: pick a command, type the JSON payload |

A double click must not start a long command twice? Add `.UseSingleFlight()` to `AddRevitCommands`: a second call of a
command that is still running gets `already_running`.
