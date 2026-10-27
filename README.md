# Davydov.Revit.Template

A `dotnet new` template for Revit 2025–2027 add-ins with a WebView2 UI. It is built on the structure and tools of
[Nice3point Revit Templates](https://github.com/Nice3point/RevitTemplates) (MIT): Revit SDK, Toolkit, Extensions,
`Host` + `ServiceDefaults`, ModularPipelines, WixSharp. On top of them come our own modules, carried over from
AnalyseTool. Design and decisions — [docs/PLAN.md](docs/PLAN.md) (in Russian).

`Davydov.Revit.Addin` is the placeholder name: a project created from the template gets the name of the new add-in instead.

## Structure

| Project | What it is |
| --- | --- |
| `source/Davydov.Revit.Addin.Launcher` | the only assembly Revit loads: the `.addin`, its own `AssemblyLoadContext`, the proxies of the ribbon buttons |
| `source/Davydov.Revit.Addin` | the add-in: `Application`, `Host`, buttons (`Commands/`), commands (`Features/`), services (`Services/`), the React app (`ClientApp/`), plumbing (`Infrastructure/`: the ribbon link to the Launcher, Debug diagnostics) |
| `source/Davydov.Revit.Addin.ServiceDefaults` | logging (Revit journal and a file log), configuration layers, JSON, DI validation |
| `source/Davydov.Revit.Hosting` | module: the command pipeline, `IRevitDispatcher`, Revit lifetime, settings |
| `source/Davydov.Revit.Hosting.WebView` | module: WebView2 windows and the dock pane, the transport to the pipeline, `revit-bridge` |
| `source/Davydov.Revit.Hosting.Mcp` | module: an MCP server inside Revit on `HttpListener`; AI clients call the commands marked with `[McpTool]` |
| `source/Davydov.Revit.Hosting.Scripting` | module: `ExecuteCode` — C# compiled with Roslyn and run inside Revit (on in Debug) |
| `source/Davydov.Revit.Hosting.Visualization` | module: geometry drawn over the views with DirectContext3D (`ShowGeometry`) |
| `build/` | the build pipeline (ModularPipelines) |
| `install/` | the `.msi` builder (WixSharp) |
| `tests/` | tests of this repository only, not part of the template |

## Build

.NET SDK 10 and Node.js 24. Configurations — `Debug.R25` … `Release.R27`:

```
dotnet build Davydov.Revit.Addin.slnx -c Debug.R26
```

A Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\<year>\`; a Release build only publishes to
`source/Davydov.Revit.Addin.Launcher/bin/Release.R2x/publish/`.

If the build fails with «workload set … has missing manifests», the .NET SDK installation is broken, not the project:
run `dotnet workload repair`, or build with the environment variable `MSBuildEnableWorkloadResolver=false`.

The whole build is the ModularPipelines pipeline in `build/`, as in Nice3point: `ClientApp` and every `Release.R2x`:

```
dotnet run -c Release --project build
```

With the tests — `Davydov.Revit.Hosting.Tests`, Vitest in `revit-bridge` and the tests inside Revit (Revit must be
closed: the tests start it themselves):

```
dotnet run -c Release --project build -- test
```

CI (`.github/workflows/Compile.yml`, on `push` to `main` and on pull requests) runs the same with `test`, but without the
tests inside Revit.

Every pipeline run checks the project boundaries (`build/Check-Boundaries.ps1`): who may reference whom, no WPF in
`Davydov.Revit.Hosting`, no `ActiveUIDocument` in `Features/` and `Services/` of the add-in, no add-in types in the
Launcher. Alone: `pwsh build/Check-Boundaries.ps1`.

Installers and the Autodesk App Store package, in `output/`:

```
dotnet run -c Release --project build -- pack
```

`…-SingleUser.msi` installs the add-in for the current user, `…-MultiUser.msi` for everyone (administrator rights),
`<product>.bundle.zip` is for the Autodesk App Store. The build starts by cleaning every `bin/` and `obj/`. The first run
installs WiX 7 and accepts its EULA (Open Source Maintenance Fee) with `wix eula accept wix7`. Product name,
UpgradeCode and vendor — in `build/appsettings.json`.

## Template

This repository is the `dotnet new davydov-revit` template itself: `.template.config/template.json` and the package
project `template/Davydov.Revit.Templates.csproj`. A new project gets no tests, `docs/`, `template/`, `LICENSE`,
`Template.yml`, and none of this repository's `README.md`, `AGENTS.md` and `CLAUDE.md`: it gets its own from
`template/content/`, with `ARCHITECTURE.md` and the agent skills in `.agents/skills/`. The `Davydov.Revit.Addin` names
and the GUIDs are replaced. Locally:

```
dotnet pack template/Davydov.Revit.Templates.csproj -o artifacts
dotnet new install artifacts/Davydov.Revit.Templates.0.1.0.nupkg
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
```

`.github/workflows/Template.yml` creates a project from the template on every pull request, checks it and builds it.

The «New project» dialog of Visual Studio replaces the `.slnx` of a solution template with a solution of its own (no
folders, only `Debug`/`Release`). So a project created there gets a classic `.sln` instead
(`template/content/Davydov.Revit.Addin.sln`, generated from the `.slnx` by the pipeline; CI fails if it is out of date),
while `dotnet new` keeps the `.slnx`. `--solution-format slnx|sln` chooses explicitly.

### Releasing the template

The package `Davydov.Revit.Templates` goes to [nuget.org](https://www.nuget.org/packages/Davydov.Revit.Templates)
through `.github/workflows/PublishTemplate.yml`, with nuget.org Trusted Publishing: no API key is stored, nuget.org
trusts that workflow file in the GitHub environment `nuget`.

* A tag `v<version>` on `main` (`git tag v0.1.0-preview.1`, `git push origin v0.1.0-preview.1`) runs the checks of
  `Template.yml`, packs the template with that version, pushes it to nuget.org and creates a GitHub release with the
  `.nupkg`; a version with `-` is a prerelease.
* A manual run (Actions → Publish template → Run workflow) takes the version and a **dry run** switch, on by default: it
  checks, packs and logs in to nuget.org, but publishes nothing.

A version on nuget.org cannot be deleted, only unlisted: try a new setup with a dry run or a prerelease first.

The template is aligned with **Nice3point.Revit.Templates 6.2.3** (the `revit-addin-sln` template with every option:
installer, bundle, GitHub Actions): `build/`, `install/`, `.github/workflows/Compile.yml`, `global.json`. To carry over
their updates: `dotnet new install Nice3point.Revit.Templates::<version>`, unpack the package from
`%USERPROFILE%\.templateengine\packages`, compare `content/Nice3point.Revit.AddIn.Solution` with this repository, then
update the version number here. Our differences from their template are listed in PLAN.md (phase 5) and in the comments
of the `build/` modules.

Rules for working on the template, for people and AI agents — [AGENTS.md](AGENTS.md) (in Russian).

Checks in a running Revit — [docs/manual-tests.md](docs/manual-tests.md) (in Russian).
