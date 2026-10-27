# Davydov.Revit.Template

[![NuGet](https://img.shields.io/nuget/vpre/Davydov.Revit.Templates)](https://www.nuget.org/packages/Davydov.Revit.Templates)
[![Downloads](https://img.shields.io/nuget/dt/Davydov.Revit.Templates)](https://www.nuget.org/packages/Davydov.Revit.Templates)
[![Stars](https://img.shields.io/github/stars/Nikola1Davydov/Davydov.Revit.Template)](https://github.com/Nikola1Davydov/Davydov.Revit.Template/stargazers)
[![Last commit](https://img.shields.io/github/last-commit/Nikola1Davydov/Davydov.Revit.Template)](https://github.com/Nikola1Davydov/Davydov.Revit.Template/commits/main)
[![Compile](https://github.com/Nikola1Davydov/Davydov.Revit.Template/actions/workflows/Compile.yml/badge.svg)](https://github.com/Nikola1Davydov/Davydov.Revit.Template/actions/workflows/Compile.yml)

A `dotnet new` template for **Revit 2025–2027** add-ins: an isolated add-in with a command pipeline, a React UI in
WebView2 and an MCP server, so AI agents can work with the running Revit. Built on
[Nice3point Revit Templates](https://github.com/Nice3point/RevitTemplates) (MIT).

```
dotnet new install Davydov.Revit.Templates::0.1.0-preview.1
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
```

**Documentation — [the wiki](https://github.com/Nikola1Davydov/Davydov.Revit.Template/wiki):** getting started,
commands, pages, ribbon buttons, AI and MCP, settings, build and release, troubleshooting.

## What you get

* **Isolation.** Revit loads only a small Launcher; the add-in and every dependency load into their own
  `AssemblyLoadContext`, so another add-in with other versions of Microsoft.Extensions.*, Serilog or WebView2 cannot
  break yours.
* **A command pipeline, like ASP.NET Core.** A command is a class in `Features/` with a request and a response; middleware
  logs it, fixes the active document for the whole call, reports progress and handles cancellation. Pages, ribbon
  buttons and AI clients call the same commands.
* **A React UI.** Vite, React and TanStack Query in WebView2 windows and a dock pane; hot reload from the Vite dev server
  in Debug, mocks for working in a normal browser.
* **AI inside Revit.** An MCP server in the add-in: Claude Code, Cursor or VS Code call the commands you mark with
  `[McpTool]`. Built-in tools let an agent see the active view, inspect elements, select and highlight them, read the log
  and — in Debug — run C# against the Revit API of the running version.
* **For agents working on your code.** `AGENTS.md`, `ARCHITECTURE.md` and skills (`add-command`, `add-page`,
  `add-ribbon-button`, `verify-in-revit`) in every created project; a boundary check fails the build when the
  architecture rules are broken.
* **Build and ship.** A ModularPipelines build for every Revit version, `.msi` installers (WixSharp), an Autodesk App
  Store bundle and GitHub Actions CI.

## Quick start

You need the .NET SDK 10, Node.js 24 and Revit 2025, 2026 or 2027.

```
dotnet new install Davydov.Revit.Templates::0.1.0-preview.1
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
cd Acme.Revit.Tools/source/Acme.Revit.Tools/ClientApp
npm install
npm run dev
```

In a second terminal, from the project folder, build for your Revit and start it:

```
dotnet build Acme.Revit.Tools.slnx -c Debug.R26
```

The Debug build deploys the add-in to `%AppData%\Autodesk\Revit\Addins\2026\`; Revit shows its ribbon tab, and the pages
come from the dev server with hot reload. Next steps — [Getting started](https://github.com/Nikola1Davydov/Davydov.Revit.Template/wiki/Getting-Started).

> The published `0.1.0-preview.1` predates the MCP server, scripting, geometry overlay and element inspection. Until the
> next preview, build the template from this repository: `dotnet pack template/Davydov.Revit.Templates.csproj -o artifacts`,
> then `dotnet new install artifacts/Davydov.Revit.Templates.0.1.0.nupkg`.

## Template options

| Option | Meaning |
| --- | --- |
| `-n <Name>` | The add-in name: projects, modules, namespaces, folders and the installer take it |
| `--vendor <Name>` | The vendor of the `.addin`, the installer and the Autodesk bundle |
| `--solution-format auto\|slnx\|sln` | `auto`: a classic `.sln` in Visual Studio's «New project» dialog, `.slnx` elsewhere |

Every project gets its own add-in id, dock pane id, installer upgrade code and MCP port.

## Working on the template

This repository is both the template and a working solution: the placeholder add-in `Davydov.Revit.Addin` builds,
passes its tests and CI. A project created from the template gets its own `README.md`, `AGENTS.md`, `ARCHITECTURE.md`
and skills from `template/content/`; tests, `docs/`, `wiki/` and the template's own workflows stay here.

```
dotnet run -c Release --project build -- test    # boundaries, ClientApp, R25–R27, tests; Revit tests only locally, Revit closed
dotnet run -c Release --project build -- pack    # .msi installers and the bundle in output/
```

* Rules for people and AI agents working on the template — [AGENTS.md](AGENTS.md), decisions and their history —
  [docs/PLAN.md](docs/PLAN.md), checks in a running Revit — [docs/manual-tests.md](docs/manual-tests.md) (in Russian).
* `Template.yml` creates a project from the template on every pull request, checks it for leftovers and builds it.
* The package goes to nuget.org from `PublishTemplate.yml` on a tag `v<version>` — pushed, or created with a release in the
  GitHub UI, which then gets the `.nupkg` attached (Trusted Publishing, no stored key); a manual run with dry run checks
  everything and publishes nothing. A version on nuget.org cannot be deleted.
* The wiki is edited in `wiki/` through pull requests; `Wiki.yml` copies it to the GitHub wiki on every push to `main`.
* The template follows **Nice3point.Revit.Templates 6.2.3** (`revit-addin-sln` with installer, bundle and GitHub
  Actions); our differences are in PLAN.md (phase 5) and the comments of `build/`.

## License

[MIT](LICENSE).
