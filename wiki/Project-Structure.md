# Project structure

## Projects

| Project | What it is | Do you change it? |
| --- | --- | --- |
| `source/Acme.Revit.Tools` | **Your add-in:** `Application`, `Host`, buttons in `Commands/`, commands in `Features/`, services in `Services/`, the React app in `ClientApp/`, plumbing in `Infrastructure/` | Yes — almost all your code goes here |
| `source/Acme.Revit.Tools.Launcher` | The only assembly Revit loads: the `.addin`, the isolated `AssemblyLoadContext`, the proxies of the ribbon buttons (`Proxies.cs`) | One line per new button |
| `source/Acme.Revit.Tools.ServiceDefaults` | Logging (Revit journal and a file), configuration layers, JSON options, DI validation | Rarely |
| `source/Acme.Revit.Tools.Hosting` | Module: the command pipeline, `IRevitDispatcher`, Revit lifetime, user settings, notifications | When the add-in needs it |
| `source/Acme.Revit.Tools.Hosting.WebView` | Module: WebView2 windows and the dock pane, the bridge to the React app | When the add-in needs it |
| `source/Acme.Revit.Tools.Hosting.Mcp` | Module: the MCP server for AI clients | Rarely |
| `source/Acme.Revit.Tools.Hosting.Scripting` | Module: `ExecuteCode` and `DescribeApi` — C# run inside Revit, for AI clients | Rarely |
| `source/Acme.Revit.Tools.Hosting.Visualization` | Module: geometry drawn over the views, nothing added to the model | Rarely |
| `build/` | The build pipeline (ModularPipelines) and the boundary check | For build steps |
| `install/` | The `.msi` builder (WixSharp) | For installer details |

The modules are your code, not a package: they carry your add-in's name and you change them as the add-in needs. They
just keep the reusable parts apart from the add-in.

## How the add-in starts

1. Revit reads `Acme.Revit.Tools.Launcher.addin` and calls `App.OnStartup` of the Launcher.
2. The Launcher creates a `PluginLoadContext` and loads `Acme.Revit.Tools.dll` into it. Every dependency —
   Microsoft.Extensions.*, Nice3point libraries, WebView2, Serilog — loads there too; the Revit API and .NET come from
   Revit. Another add-in with other versions of the same packages cannot clash with yours.
3. The Launcher creates `Acme.Revit.Tools.Application` by name — the only reflection call into the add-in.
4. `Application` starts the `Host` (`Host.cs`) and builds the ribbon.

If a step fails, the Launcher writes the exception to `%LOCALAPPDATA%\Acme.Revit.Tools\logs\<year>\startup-error.txt` and
Revit reports the failed add-in.

### Why buttons need proxies

Revit creates a button's class from the assembly path of the button. A button pointing to `Acme.Revit.Tools.dll` would
load it a second time, outside the isolated context. So every button points to a same-named proxy class in the Launcher,
and the proxy forwards the click into the add-in. See [Ribbon buttons](Ribbon-Buttons).

## Host

`Host.cs` is the `Program.cs` of the add-in: a .NET Generic Host with dependency injection, configuration and logging.

```csharp
builder.AddServiceDefaults();                          // logging, configuration, JSON, DI validation
builder.AddRevitHosting(application);                  // IRevitDispatcher, IRevitLifetime, IUserNotifier
builder.AddRevitCommands(commands => commands
    .FromAssemblyOf<Application>()                     // every command class of the add-in
    .AddScripting(builder)                             // ExecuteCode / DescribeApi, Debug only by default
    .UseCommandLogging()
    .Use<DocumentCheckMiddleware>());
builder.AddWebView(application, webView => webView.DockPane(id, "Acme.Revit.Tools", "/dock"));
builder.AddMcp(application, basePort: 4xxxx);          // the MCP server, Debug only by default
builder.AddVisualization();                            // IGeometryOverlay
```

Classes get their dependencies through the constructor. Only ribbon buttons — Revit creates them — use
`Host.GetService<T>()`.

## Where your code goes

| You are writing… | Put it in | See |
| --- | --- | --- |
| Code that reads or changes the model | `Features/<Slice>/` as a command | [Commands](Commands) |
| A service several commands share | `Services/` and register it in `Host.cs` | |
| A ribbon button | `Commands/` + one line in the Launcher's `Proxies.cs` | [Ribbon buttons](Ribbon-Buttons) |
| A page or a window | `ClientApp/src/` | [Pages](Pages) |
| Settings | an options class, a section in `appsettings.json` | [Settings and logging](Settings-and-Logging) |

## Rules the build enforces

`build/Check-Boundaries.ps1` runs on every pipeline build and fails it on:

* a project or package reference that its table does not allow — add the reference to the table first;
* WPF in the headless modules (`Hosting`, `Hosting.Mcp`, `Hosting.Scripting`, `Hosting.Visualization`) or in `Features/`
  and `Services/` of the add-in — the UI is the React app, messages go through `IUserNotifier`;
* `ActiveUIDocument` in `Features/` and `Services/` — commands use `ActiveDocument`; a line that really needs it is
  marked `// AllowActiveDocument: <reason>`;
* a type of the add-in or the modules in the Launcher — they would load outside the isolated context.

Run it alone: `pwsh build/Check-Boundaries.ps1`.
