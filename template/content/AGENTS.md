# Davydov.Revit.Addin

A Revit 2025–2027 add-in: Nice3point SDK and Toolkit, an isolated Launcher, a command pipeline in the style of ASP.NET
Core and a React UI in WebView2. How it fits together — [ARCHITECTURE.md](ARCHITECTURE.md).

## Skills

Read the skill before the task it covers:

| Task | Skill |
| --- | --- |
| A ribbon button, its availability or icon | [.agents/skills/add-ribbon-button/SKILL.md](.agents/skills/add-ribbon-button/SKILL.md) |
| A command the pages or a button call: reading or changing the model | [.agents/skills/add-command/SKILL.md](.agents/skills/add-command/SKILL.md) |
| A page, a window or the dock pane | [.agents/skills/add-page/SKILL.md](.agents/skills/add-page/SKILL.md) |

## Non-negotiables

* The Revit API is used only on the Revit thread. A command runs on the thread pool and reaches the model only through
  `ActiveDocument` or `IRevitDispatcher`; a ribbon button (`ExternalCommand.Execute`) already runs on the Revit thread.
* A command takes the document from `ActiveDocument`, never from `ActiveUIDocument`: the user may switch documents
  while the command runs, and `ActiveDocument` then fails with `document_changed` instead of touching another model.
  `build/Check-Boundaries.ps1` rejects `ActiveUIDocument` in `Features/` and `Services/`; a line that really needs it is
  marked `// AllowActiveDocument: <reason>`.
* `Features/` and `Services/` contain no WPF (`System.Windows`, `MessageBox`): the UI is the React app, and a message to
  the user goes through `IUserNotifier`.
* The Launcher (`source/Davydov.Revit.Addin.Launcher`) uses no type of the add-in or the modules — only names in
  strings. Their types would load into the Revit context and bypass the add-in's `AssemblyLoadContext`.
* `Davydov.Revit.Hosting` and `Davydov.Revit.Hosting.Mcp` stay headless: no WPF.
* An AI client (MCP) sees only commands marked with `[McpTool]`: open a command to it deliberately, never by default.
* A ribbon button is a class in `Commands/` and a same-named proxy line in the Launcher's `Proxies.cs`. A button class
  keeps its name and namespace: Revit binds keyboard shortcuts and the Quick Access Toolbar to them.
* Logging goes through source-generated `[LoggerMessage]` methods, not the `Log*` extension methods of `ILogger`.
* A project reference or a package reference goes into the table of `build/Check-Boundaries.ps1` first; the pipeline
  fails on a reference the table does not allow.

## Build and check

```
dotnet build Davydov.Revit.Addin.slnx -c Debug.R26
dotnet run -c Release --project build
dotnet run -c Release --project build -- pack
```

A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\` and loads the pages from the Vite dev
server (`npm run dev` in `source/Davydov.Revit.Addin/ClientApp`). The pipeline checks the boundaries, builds `ClientApp`
and every `Release.R2x`; `pack` adds the `.msi` installers and the Autodesk bundle in `output/`.
