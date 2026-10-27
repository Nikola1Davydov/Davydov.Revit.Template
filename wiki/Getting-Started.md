# Getting started

## Requirements

* Windows with Revit 2025, 2026 or 2027.
* .NET SDK 10 (`dotnet --version`).
* Node.js 24 with npm, for the React pages.
* An editor: Visual Studio 2022/2026, Rider or VS Code.

## Install the template

```
dotnet new install Davydov.Revit.Templates::0.1.0-preview.1
```

The published `0.1.0-preview.1` predates the MCP server, scripting, geometry overlay and element inspection. For the
current state, build the template from the repository:

```
git clone https://github.com/Nikola1Davydov/Davydov.Revit.Template
cd Davydov.Revit.Template
dotnet pack template/Davydov.Revit.Templates.csproj -o artifacts
dotnet new install artifacts/Davydov.Revit.Templates.0.1.0.nupkg
```

Before installing another version: `dotnet new uninstall Davydov.Revit.Templates`.

## Create a project

```
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
```

| Option | Meaning |
| --- | --- |
| `-n` | The add-in name. Projects, modules (`Acme.Revit.Tools.Hosting`, …), namespaces, folders, the `.addin` and the installer take it. Use a dotted name like `Company.Revit.Product`. |
| `--vendor` | The vendor: `VendorId` of the `.addin`, the manufacturer of the installer and of the Autodesk bundle. |
| `--solution-format` | `auto` (default): a classic `.sln` in Visual Studio's «New project» dialog, a `.slnx` elsewhere; `slnx` or `sln` to choose. |

Every project gets its own add-in id, dock pane id, installer upgrade code and MCP port, so two add-ins from the template
run side by side in one Revit.

The template is also in Visual Studio's «New project» dialog (search for «davydov-revit»). There you get a `.sln`:
Visual Studio would replace a `.slnx` with a solution of its own; File → Save As converts it later.

## Run it in Revit

1. Start the Vite dev server for the pages — Debug builds load them from it:

   ```
   cd Acme.Revit.Tools/source/Acme.Revit.Tools/ClientApp
   npm install
   npm run dev
   ```

2. Build for your Revit version. Configurations are `Debug.R25` … `Debug.R27` and `Release.R25` … `Release.R27`:

   ```
   dotnet build Acme.Revit.Tools.slnx -c Debug.R26
   ```

   A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\2026\`. Close Revit first: a running Revit
   holds the add-in files.

3. Start Revit and open a project. The `Acme.Revit.Tools` tab has a «Status» button; the «Dev» panel (Debug only) opens
   the Dev page in the dock pane — the live log, any command with a JSON payload, and how to connect an AI client.

To debug, make the Launcher project the startup project and press F5: it has `LaunchRevit`, so the IDE starts
`C:\Program Files\Autodesk\Revit <year>\Revit.exe` of the chosen configuration. Or attach to a running `Revit.exe`.
Breakpoints in the add-in work as usual. F12 in a page opens the WebView2 DevTools.

## What next

* Write your first [command](Commands) in `Features/` — the code that reads or changes the model.
* Show it on a [page](Pages) or behind a [ribbon button](Ribbon-Buttons).
* Mark it with `[McpTool]` and call it from [Claude Code](AI-and-MCP).
* Remove what you do not need: the sample command `Features/Sample/CountElementsByCategory.cs`, the «Status» button,
  `AllowAnyNavigation()` in `Host.cs` (it lets any web page load in the WebView — remove it unless you need it).

Working with an AI agent on the code? The project has `AGENTS.md` (rules), `ARCHITECTURE.md` and skills in
`.agents/skills/` — Claude Code reads them through `CLAUDE.md`.
