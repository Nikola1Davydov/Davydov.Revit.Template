# Davydov.Revit.Templates

A `dotnet new` template for Revit 2025–2027 add-ins with a React UI in WebView2, built on
[Nice3point Revit Templates](https://github.com/Nice3point/RevitTemplates): the Revit SDK, Toolkit and `Host` +
`ServiceDefaults`, plus

* an isolated Launcher: the add-in and its dependencies load into their own `AssemblyLoadContext`, so other add-ins
  cannot clash with their versions;
* a command pipeline in the style of ASP.NET Core, with middleware, the active document fixed for the whole call, and
  progress and cancellation for long work;
* a React app (Vite, TanStack Query) in WebView2 windows and a dock pane;
* a ModularPipelines build, `.msi` installers (WixSharp), an Autodesk App Store bundle and GitHub Actions CI;
* `AGENTS.md`, `ARCHITECTURE.md` and skills for AI agents.

## Usage

```
dotnet new install Davydov.Revit.Templates
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
```

`-n` is the name of the add-in: projects, modules, namespaces, folders and the installer take it. `--vendor` is the
vendor of the `.addin`, the installer and the bundle. Every project gets its own add-in id, dock pane id and installer
upgrade code.

The template works from the «New project» dialog of Visual Studio too. There the solution is a classic `.sln` — Visual
Studio would replace a `.slnx` with a solution of its own; File → Save As converts it to `.slnx`. `dotnet new` creates a
`.slnx`; `--solution-format sln` asks for the `.sln` instead.

The created project explains itself in its `README.md` and `ARCHITECTURE.md`. Source, issues and changes:
[github.com/Nikola1Davydov/Davydov.Revit.Template](https://github.com/Nikola1Davydov/Davydov.Revit.Template).
