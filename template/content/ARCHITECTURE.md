# Architecture

How the add-in starts, where the code goes and why. The rules an agent must not break are in [AGENTS.md](AGENTS.md).

## Projects

| Project | Role |
| --- | --- |
| `source/Davydov.Revit.Addin.Launcher` | The only assembly Revit loads. Starts the add-in in its own `AssemblyLoadContext`; holds the proxies of the ribbon buttons. References nothing but the Revit API. |
| `source/Davydov.Revit.Addin` | The add-in: `Application`, `Host`, buttons (`Commands/`), commands (`Features/`), services (`Services/`), the React app (`ClientApp/`), plumbing (`Infrastructure/`). |
| `source/Davydov.Revit.Addin.ServiceDefaults` | Logging, configuration layers, JSON options, DI validation. |
| `source/Davydov.Revit.Hosting` | Module: the command pipeline, `IRevitDispatcher`, Revit lifetime, user settings, notifications. No WPF. |
| `source/Davydov.Revit.Hosting.WebView` | Module: WebView2 windows, the dock pane, the transport to the pipeline and `revit-bridge` for the pages. |
| `build/` | The build pipeline (ModularPipelines): boundary check, `ClientApp`, compile, `pack`. |
| `install/` | The `.msi` builder (WixSharp). |

The modules are the add-in's own code, not a dependency: change them as the add-in needs. Only `Davydov.Revit.Hosting`
stays headless, so the pipeline can run without the UI.

## Startup and isolation

1. Revit loads `Davydov.Revit.Addin.Launcher.addin` and calls `App.OnStartup` of the Launcher.
2. The Launcher creates a `PluginLoadContext` from its `deps.json` and loads `Davydov.Revit.Addin.dll` into it. Every
   dependency — Microsoft.Extensions.*, Nice3point libraries, WebView2, Serilog — loads there too, so another add-in with
   other versions of them cannot clash. The Revit API and the shared framework come from Revit's own context.
3. The Launcher publishes its path and the namespace of its proxies through `AppContext` (keys end with the add-in name)
   and creates `Davydov.Revit.Addin.Application` by name — the single reflection call into the add-in.
4. `Application` builds and starts the `Host` (`Host.cs`), then the ribbon.

If any step fails, the Launcher writes the exception to `%LOCALAPPDATA%\Davydov.Revit.Addin\logs\<year>\startup-error.txt`
and rethrows it; Revit reports the failed external application itself.

Revit creates the class of a ribbon button from the assembly path of the button. A button that pointed to the add-in
assembly would load it a second time, outside the isolation, so every button points to a same-named proxy in the
Launcher (`Proxies.cs`), and the proxy forwards the click into the add-in's context. `AddButton<T>` checks at startup
that the proxy exists.

## Host

`Host.cs` is the `Program.cs` of the add-in: a Generic Host with dependency injection, configuration and logging.

* `AddServiceDefaults()` — logging, configuration layers with `IUserSettings`, JSON options, DI validation at `Build()`.
* `AddRevitHosting(application)` — `IRevitDispatcher`, `IRevitLifetime`, `IUserNotifier`.
* `AddRevitCommands(...)` — the command registry, the middleware of the pipeline, `ICommandExecutor` and `ICommandSender`.
* `AddWebView(application, ...)` — windows, the dock pane and the page source.

Classes get their dependencies through the constructor. Only ribbon buttons, which Revit creates itself, resolve
services with `Host.GetService<T>()`.

## Commands and the pipeline

A command is a class in `Features/<slice>/` that implements `ICommand<TResponse>` or `ICommand<TRequest, TResponse>`,
like a controller of an ASP.NET Core app. The registry finds command classes by reflection at startup and reports every
problem at once; the command name is the class name without the `Command` suffix, or the name from `[Command]`.

A call goes `ExceptionHandling → CommandLogging → DocumentCheckMiddleware → command` and always runs on the thread pool,
never on the Revit thread. Exceptions become a result with an error code: `CommandException(code, message)` for an
expected refusal, anything else becomes `failed`. Pages call commands through `revit-bridge`, buttons through
`ICommandSender`; a failure of a button call is shown with `IUserNotifier`.

## Revit thread and the document

The Revit API works only on the Revit thread. A command reaches it through `IRevitDispatcher.InvokeAsync(...)` — one
long-lived external event — or, for the active document, through `ActiveDocument`:

* `InvokeAsync(document => ...)` and `InvokeUIAsync(uiDocument => ...)` run work on the Revit thread; transactions are
  allowed inside.
* The first access fixes the document of the call. If the user switches documents before a later access, that access
  fails with `document_changed`; with no document open, with `no_document`.
* `DocumentCheckMiddleware` fixes the document up front from the `Document` header a page sends, so data loaded from one
  document is never applied to another.
* `InvokeBatchedAsync(items, batchSize, work, progress, cancellationToken)` runs long work in batches: between batches
  Revit gets control back, the page shows progress and the user can cancel.

## Pages

`ClientApp/` is a React app (Vite, TanStack Query). A page is a hash route: `IWindows.Open("/route")` shows it in a
window — one window per route — and `IWindows.ShowDockPane("/route")` in the dock pane. In Debug the pages come from
the Vite dev server (`ClientApp:DevServerUrl`, `http://localhost:5173` by default), in Release from `ClientApp/dist`
through the virtual host `https://app.davydov-revit-addin.local`.

`revit-bridge` sends a request over WebView2 messaging and matches the response by its id; progress and cancellation go
the same way. The hooks in `ClientApp/src/revit/hooks.ts` add the `Document` header: `useActiveDocument()`,
`useCommand(name, payload)` to read, `useCommandMutation(name)` to run with progress and `cancel()`. Outside Revit, in a
normal browser, `ClientApp/src/mocks.ts` answers the calls.

Any page loaded in the WebView can call commands, so by default only the add-in's own pages load: another address opens
in the system browser, and messages from another origin are dropped. `AllowAnyNavigation()` in `AddWebView` turns the
protection off — `Host.cs` of this template calls it.

## Settings and logs

Configuration layers, a later one overrides an earlier one:

1. `appsettings.json` next to the add-in;
2. `appsettings.{Environment}.json` — `Development` in Debug, `Production` in Release;
3. `%AppData%\Davydov.Revit.Addin\<year>\usersettings.json` — what the user changed, written by `IUserSettings.UpdateAsync`;
4. environment variables prefixed with the add-in name in upper case, dots replaced with underscores.

Code reads settings with `IOptions<T>` / `IOptionsMonitor<T>`. `IUserSettings.UpdateAsync` writes the whole section, so
once written, `appsettings.json` no longer changes that section for this user.

Logs go to the Revit journal (from `Information` in Debug, from `Warning` in Release) and to
`%LOCALAPPDATA%\Davydov.Revit.Addin\logs\<year>\log-<date>.txt` (from `Information`, a file per day). Both levels are set in
the `Logging` section.

## Boundaries

`build/Check-Boundaries.ps1` runs on every pipeline run and fails it on:

* a project or package reference that the table at the top of the script does not allow;
* WPF in `Davydov.Revit.Hosting`, or in `Features/` and `Services/` of the add-in;
* `ActiveUIDocument` in `Features/` and `Services/`, outside `ActiveDocument.cs` and lines marked `// AllowActiveDocument:`;
* a type of the add-in or the modules in the Launcher code.

Comments and strings do not count. Run it alone with `pwsh build/Check-Boundaries.ps1`.
