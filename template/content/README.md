# Davydov.Revit.Addin

Revit 2025–2027 add-in, created from the `davydov-revit` template: [Nice3point](https://github.com/Nice3point/RevitTemplates)
SDK, Toolkit and `Host` + `ServiceDefaults`, an isolated Launcher, a command pipeline and a React UI in WebView2.

## Structure

| Project | What it is |
| --- | --- |
| `source/Davydov.Revit.Addin.Launcher` | the only assembly Revit loads: the `.addin`, its own `AssemblyLoadContext`, proxies of the ribbon buttons (`Proxies.cs`) |
| `source/Davydov.Revit.Addin` | the add-in: `Application`, `Host`, ribbon buttons (`Commands/`), commands (`Features/`), services (`Services/`), the React app (`ClientApp/`) |
| `source/Davydov.Revit.Addin.ServiceDefaults` | logging (Revit journal and a file log), configuration layers, JSON, DI validation |
| `source/Davydov.Revit.Hosting` | module: the command pipeline, `IRevitDispatcher`, Revit lifetime, user settings, ribbon wrappers |
| `source/Davydov.Revit.Hosting.WebView` | module: WebView2 windows and the dock pane, the bridge to React (`revit-bridge`) |
| `build/` | the build pipeline (ModularPipelines) |
| `install/` | the `.msi` builder (WixSharp) |

## Build

.NET SDK 10 and Node.js 24. Configurations — `Debug.R25` … `Release.R27`:

```
dotnet build Davydov.Revit.Addin.slnx -c Debug.R26
```

A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\` and loads the pages from the Vite dev
server: run `npm install` and `npm run dev` in `source/Davydov.Revit.Addin/ClientApp` first.

The whole build — `ClientApp` and every `Release.R2x` — and the project boundary check (`build/Check-Boundaries.ps1`):

```
dotnet run -c Release --project build
```

Installers and the Autodesk App Store package, in `output/`:

```
dotnet run -c Release --project build -- pack
```

`…-SingleUser.msi` installs for the current user, `…-MultiUser.msi` for everyone, `Davydov.Revit.Addin.bundle.zip` is
for the Autodesk App Store. The first run installs WiX 7 and accepts its EULA (Open Source Maintenance Fee) with
`wix eula accept wix7`. Product name, UpgradeCode and vendor — in `build/appsettings.json`.

CI — `.github/workflows/Compile.yml`, on `push` to `main` and on pull requests.

## A new ribbon button

1. A class in `Commands/` derived from `ExternalCommand`, added in `Application` with `AddButton<T>`.
2. A one-line proxy in the Launcher, `Proxies.cs`: `[Transaction(TransactionMode.Manual)] public sealed class MyCommand : ProxyCommand;`
   — the add-in reports a missing proxy at startup.

## A new command for the pages

A class in `Features/` implementing `ICommand<TRequest, TResponse>` (or `ICommand<TResponse>`): it is registered by
reflection, gets its dependencies through the constructor and takes the document from `ActiveDocument`, never from
`ActiveUIDocument`. The pages call it with `useCommand` / `useCommandMutation` from `ClientApp/src/revit`.
