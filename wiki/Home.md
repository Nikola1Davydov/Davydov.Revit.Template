# Davydov.Revit.Template

A `dotnet new` template for Revit 2025–2027 add-ins: an isolated add-in with a command pipeline, a React UI in WebView2
and an MCP server for AI agents. This wiki is for people who build an add-in from the template.

The examples use an add-in created as `dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme`; in your project the
names are yours.

## Start here

1. [Getting started](Getting-Started) — install the template, create a project, see it in Revit.
2. [Project structure](Project-Structure) — what each project is for, how the add-in starts, where your code goes.
3. [Commands](Commands) — the code that reads or changes the model; everything else calls it.

## Build your add-in

* [Ribbon buttons](Ribbon-Buttons) — a button, its proxy in the Launcher, icon and availability.
* [Pages](Pages) — React pages in windows and the dock pane, calling commands, working without Revit.
* [Settings and logging](Settings-and-Logging) — configuration layers, user settings, logs, the Dev page.
* [AI and MCP](AI-and-MCP) — let Claude Code, Cursor or VS Code work with the running Revit; the built-in tools.

## Ship it

* [Build and release](Build-and-Release) — the build pipeline, tests, installers, the Autodesk bundle, CI.
* [Troubleshooting](Troubleshooting) — the problems people actually run into.

## In one picture

```
Revit ──loads──▶ Launcher (.addin, proxies) ──own AssemblyLoadContext──▶ Acme.Revit.Tools
                                                                          │
                     ribbon button ─┐                                     │ Host: DI, config, logging
                     React page   ──┼──▶ command pipeline ──▶ Features/ ──┤
                     AI client (MCP)┘    (middleware)        commands     │ ActiveDocument ──▶ Revit API
```

A ribbon button, a page and an AI client all call the same commands; only the commands touch the Revit API.

The created project explains itself too: `README.md`, `ARCHITECTURE.md`, `AGENTS.md` and the skills in `.agents/skills/`.
Source and issues: [github.com/Nikola1Davydov/Davydov.Revit.Template](https://github.com/Nikola1Davydov/Davydov.Revit.Template).
