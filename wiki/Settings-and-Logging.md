# Settings and logging

## Configuration layers

A later layer overrides an earlier one:

1. `appsettings.json` next to the add-in — your defaults;
2. `appsettings.{Environment}.json` — `Development` in Debug, `Production` in Release;
3. `%AppData%\Acme.Revit.Tools\<year>\usersettings.json` — what the user changed in your UI;
4. environment variables: the add-in name in upper case with dots replaced by underscores, then the key —
   `ACME_REVIT_TOOLS_Mcp__Enabled=true`.

## Read settings

An options class and a section:

```csharp
public sealed class ExportOptions
{
    public const string SectionName = "Export";
    public string Folder { get; set; } = "";
    public bool OpenAfterExport { get; set; } = true;
}
```

```csharp
// Host.cs
builder.Services.Configure<ExportOptions>(builder.Configuration.GetSection(ExportOptions.SectionName));
```

```json
// appsettings.json
{ "Export": { "Folder": "C:\\Exports", "OpenAfterExport": true } }
```

Take `IOptions<ExportOptions>` in a constructor, or `IOptionsMonitor<ExportOptions>` to see changes while Revit runs.

## Save what the user changed

```csharp
await userSettings.UpdateAsync<ExportOptions>(ExportOptions.SectionName, options => options.Folder = folder);
```

`IUserSettings` writes the section to `usersettings.json`; the file is reloaded, so `IOptionsMonitor` sees the change at
once. It writes the whole section with its current values — after the first save, a new default in `appsettings.json`
of a later release no longer reaches that user for that section. Not for secrets: use Windows DPAPI for those.

## Logging

Logs go to two places:

* the **Revit journal** — from `Information` in Debug, from `Warning` in Release;
* a **file per day**, `%LOCALAPPDATA%\Acme.Revit.Tools\logs\<year>\log-<date>.txt`, from `Information`.

Both levels are set in the `Logging` section of `appsettings.json`. Write log calls as source-generated methods:

```csharp
[LoggerMessage(LogLevel.Information, "Exported {Count} sheets to {Folder}")]
private static partial void LogExported(ILogger logger, int count, string folder);
```

Every command call is logged by the pipeline already: `Command GetFamilies (webview2) succeeded in 12 ms`.

## Messages to the user

`IUserNotifier.Error(title, message)` and `Info(title, message)` show a Revit task dialog from any thread. `Features/` and
`Services/` use it instead of WPF; a failed command started from a button is shown this way automatically.

## The Dev page (Debug)

The «Dev» panel of a Debug build has a «Dev page» button. It opens the `/dev` route in the dock pane:

* **Logs** — the live log of the add-in, with level filter;
* **Commands** — every registered command; pick one, type a JSON payload, run it, see the result or the error;
* **MCP** — the address and the commands to connect an AI client (see [AI and MCP](AI-and-MCP)).

In Release the page, its button and its in-memory log buffer are not built.
