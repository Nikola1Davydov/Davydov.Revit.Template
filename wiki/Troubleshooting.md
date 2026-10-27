# Troubleshooting

## The build fails: the file is used by another process

A Debug build copies the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\`, and a running Revit holds those files.
Close Revit, build, start it again. A Release build does not deploy and works with Revit open.

## «workload set … has missing manifests»

The .NET SDK installation is broken, not the project. Run `dotnet workload repair`, or build with the environment
variable `MSBuildEnableWorkloadResolver=false`.

## Revit says the add-in failed to start

The full exception is in `%LOCALAPPDATA%\Acme.Revit.Tools\logs\<year>\startup-error.txt`. The usual causes:

* **«The Launcher has no proxy for '…'»** — a new ribbon button without its line in the Launcher's `Proxies.cs`. See
  [Ribbon buttons](Ribbon-Buttons).
* **A command registration error** — the registry lists every broken command at once: a duplicate name, a request type
  it cannot create.
* **A DI validation error** — a service takes a dependency nobody registered; register it in `Host.cs`.

## A button says «… failed to start»

The add-in did not start, so its buttons have nothing to call. Read `startup-error.txt` as above.

## The window or the dock pane is blank (Debug)

Debug builds load the pages from the Vite dev server. Start it: `npm run dev` in `source/Acme.Revit.Tools/ClientApp`,
then reopen the window. Another port? Set `ClientApp:DevServerUrl` in the settings.

## The page shows mock data in Revit

It does not run in Revit then: a normal browser uses `ClientApp/src/mocks.ts`. In Revit, F12 opens the WebView2 DevTools —
check the console and the address.

## A command fails with `document_changed`

The user switched documents while the command ran, or the page sent data loaded from another document. That is the
protection working: the page reloads its data. If you see it from your own code, the command touched `ActiveDocument`
after a switch — read everything in one `InvokeAsync`.

## Revit freezes while a command runs

The work inside one `InvokeAsync` blocks Revit until it returns. Split long work with `InvokeBatchedAsync`: between
batches Revit gets control back and the user can cancel. See [Commands](Commands).

## The AI client cannot connect: `ECONNREFUSED`

* Revit is not running, or the add-in failed to start.
* The server is off: it is on only in Debug, unless `Mcp:Enabled` is `true`.
* The port is busy — another Revit of the same version runs. The log says so at startup.

`%LOCALAPPDATA%\Acme.Revit.Tools\<year>\mcp.json` exists only when the server started.

## The AI client does not see a new command

* The command is not marked with `[McpTool]`, or has no description.
* The client read the tool list before the change: reconnect it (`/mcp` in Claude Code) after Revit restarts.

## `ExecuteCode` or `DescribeApi` is missing

Scripting is on only in Debug. In Release, `Scripting:Enabled` turns it on — think twice: any code runs with the user's
rights.

## `npm ci` fails with `EPERM` in the pipeline

A running Vite dev server holds files in `node_modules`. Stop `npm run dev` before the pipeline build, or run the
pipeline without `CI=true` (it then uses `npm install`).

## Where things are

| What | Where |
| --- | --- |
| The deployed add-in (Debug) | `%AppData%\Autodesk\Revit\Addins\<year>\` |
| Logs | `%LOCALAPPDATA%\Acme.Revit.Tools\logs\<year>\log-<date>.txt` |
| Startup errors | `%LOCALAPPDATA%\Acme.Revit.Tools\logs\<year>\startup-error.txt` |
| User settings | `%AppData%\Acme.Revit.Tools\<year>\usersettings.json` |
| MCP connection | `%LOCALAPPDATA%\Acme.Revit.Tools\<year>\mcp.json` |
| Script journal | `%LOCALAPPDATA%\Acme.Revit.Tools\scripts\<date>.jsonl` |
| Installers | `output/` after `pack` |
