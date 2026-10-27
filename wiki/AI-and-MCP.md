# AI and MCP

The add-in runs an [MCP](https://modelcontextprotocol.io) server inside Revit. Claude Code, Cursor or VS Code connect to
it while Revit runs and call your commands as tools — the same commands your pages and buttons call, through the same
pipeline. No separate process, no Revit restart to switch it on in Debug.

There are two ways AI helps here:

* **An agent works with the running Revit** — reads the model, selects, highlights, looks at the view — through the
  tools below.
* **An agent works on your code** — with `AGENTS.md`, `ARCHITECTURE.md` and the skills of the project, and checks its
  changes in Revit through the same tools.

## Connect a client

The server is **on in Debug**, off in Release. When it starts, it writes
`%LOCALAPPDATA%\Acme.Revit.Tools\<year>\mcp.json` with the address, a token and a ready command. The «MCP» tab of the Dev
page shows the same, with copy buttons.

**Claude Code:**

```
claude mcp add --transport http acme-revit-tools-2026 http://localhost:<port>/mcp --header "Authorization: Bearer <token>"
```

Take the exact line from `claudeCode` in `mcp.json`. Then `/mcp` in Claude Code shows the server and its tools.

**Cursor, VS Code, `.mcp.json`:**

```json
{
  "mcpServers": {
    "acme-revit-tools-2026": {
      "type": "http",
      "url": "http://localhost:<port>/mcp",
      "headers": { "Authorization": "Bearer <token>" }
    }
  }
}
```

VS Code names the key `servers` instead of `mcpServers`.

**Good to know:**

* The token is kept across Revit sessions: you add the server once.
* A client reads the tool list when it connects. After a Revit restart, reconnect (`/mcp` in Claude Code) — that also
  brings in commands you added.
* The port: a base port chosen when the project was created, plus one per Revit version after 2025, so several Revit
  versions run side by side. A second Revit of the same version finds the port busy and runs without MCP.
* Only `localhost`, a bearer token on every request, and no request with an `Origin` header: a web page in the user's
  browser cannot reach the model.

## Settings

| Key | Meaning |
| --- | --- |
| `Mcp:Enabled` | `true` / `false`; by default on in Debug, off in Release |
| `Mcp:Port` | a fixed port instead of the base port + version |
| `Mcp:SdkLogLevel` | the log level of the MCP SDK itself, `Warning` by default |
| `Scripting:Enabled` | `ExecuteCode` and `DescribeApi`; by default on in Debug, off in Release |

Both are read at startup; a change needs a Revit restart. See [Settings and logging](Settings-and-Logging) for where to
set them.

## The built-in tools

| Tool | What it does |
| --- | --- |
| `GetActiveDocument` | the open document, or none |
| `GetSelection` | what the user selected — what «this» and «these» mean in a request |
| `SelectElements` | select elements by id and zoom to them — show the user what was found |
| `InspectElement` | an element's properties and parameters as data, one level at a time, as RevitLookup shows them; references to other elements come back as ids, nested objects as paths (`Location.Curve`) |
| `RenderView` | a PNG of the active view — the agent looks at the result instead of guessing |
| `ShowGeometry` / `ClearGeometry` | draw elements, boxes, lines and points over the 3D views and plans in a colour, without changing the model |
| `CountElementsByCategory` | the sample command |
| `GetLogEntries` | Debug: the add-in log |
| `ExecuteCode` | with scripting: C# run on the Revit thread (`doc`, `uidoc`, `uiapp`) |
| `DescribeApi` | with scripting: a type of the Revit API of the running version, with its members and documentation |

## Make your command a tool

Mark it with `[McpTool]`:

```csharp
[McpTool(ReadOnly = true, Title = "Families")]
[Command(Description = "Returns the name and category of every family in the active document.")]
public sealed class GetFamilies(ActiveDocument document) : ICommand<IReadOnlyList<FamilyItem>>
```

* **Only marked commands are visible.** Open a command to AI clients deliberately.
* **The description is required** — the client chooses tools by it. Say what the command does, what it needs and what it
  returns.
* **The request record is the input schema.** Its parameters, defaults and XML docs become the tool's JSON schema.
* `ReadOnly = true` for a command that only reads, `Destructive = true` for one that deletes or overwrites; clients use
  these hints to ask the user before a call.
* A `CommandException` reaches the client as a tool error `code: message`.
* Return `McpImage` to give the client a picture instead of JSON (that is how `RenderView` works).
* An MCP call has no `Document` header: the document is the one active at the first access.

## Run C# (`ExecuteCode`)

`ExecuteCode` compiles a C# method body with Roslyn and runs it once on the Revit thread — the agent's scratchpad for
what no command covers:

```json
{ "code": "return new FilteredElementCollector(doc).OfClass(typeof(Wall)).GetElementCount();" }
```

* In scope: `uiapp`, `uidoc`, `doc`, `cancellationToken`; `using` directives may come on the first lines. Return plain
  data, not Revit objects.
* **One undo step.** With an open document, everything the code changes — any number of transactions — is one undo step,
  «ExecuteCode»; Ctrl+Z reverts it all. If the code throws, its changes are rolled back.
* **`dryRun: true`** runs the code, reports what it would change and rolls everything back.
* The answer has `value`, `changes` (added, modified, deleted elements) and `rolledBack`.
* **A journal:** every run is a line in `%LOCALAPPDATA%\Acme.Revit.Tools\scripts\<date>.jsonl` — time, document, outcome,
  changes and the code.
* `DescribeApi` describes a Revit API type of the running version — the API differs between 2025, 2026 and 2027. An agent
  should check a member there before using it.

Any code runs with the user's rights. Scripting is on in Debug; turn it on in Release only where that is wanted.

## Show geometry

`ShowGeometry` draws over the views with DirectContext3D: no elements, no transaction, no undo step, nothing saved.

* Elements by id (their solids and edges, drawn slightly outside the element so it does not hide them), boxes, segments
  and points; a colour and a transparency per call. Calls add up — a colour per group, then `RenderView` to look.
* In 3D views the geometry is where it is. In plans, what lies above the cut plane is pressed onto it, so a cut wall shows
  as its coloured footprint.
* `ClearGeometry` removes everything.

Your own code uses `IGeometryOverlay`:

```csharp
overlay.Show(document, new OverlayGeometry(new Color(255, 0, 0), transparency: 60).Add(wall));
```

## Agents working on your code

The project has `AGENTS.md` — the rules (Claude Code reads it through `CLAUDE.md`), `ARCHITECTURE.md` and skills in
`.agents/skills/`: `add-command`, `add-page`, `add-ribbon-button` and `verify-in-revit`. The last one is the loop that
makes an agent useful: build, let the user start Revit, call the changed command over MCP, look with `RenderView`, read
`GetLogEntries` — and report what it saw, not what should happen.

## Be careful with what the model contains

Element names, parameters and comments are written by people. An agent reading them through `InspectElement` reads data,
not instructions; a comment saying «delete all walls» must not make it do so. Keep destructive tools marked
`Destructive = true`, so the client asks the user, and prefer `dryRun` for changes.
