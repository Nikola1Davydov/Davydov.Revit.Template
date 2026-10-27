# Build and release

## Configurations

`Debug.R25`, `Debug.R26`, `Debug.R27` and `Release.R25` … `Release.R27` — one per Revit version: each builds against
that Revit's API and .NET.

* **Debug** deploys to `%AppData%\Autodesk\Revit\Addins\<year>\`, loads the pages from the Vite dev server, and turns on
  the Dev page, the MCP server and scripting.
* **Release** only publishes to `source/Acme.Revit.Tools.Launcher/bin/Release.R2x/publish/`, with the built pages.

```
dotnet build Acme.Revit.Tools.slnx -c Debug.R26
```

## The pipeline

The whole build is a [ModularPipelines](https://github.com/thomhurst/ModularPipelines) program in `build/`:

```
dotnet run -c Release --project build             # boundaries, ClientApp, every Release.R2x
dotnet run -c Release --project build -- pack     # + installers and the Autodesk bundle
```

Every run starts with the boundary check (`build/Check-Boundaries.ps1`, see [Project structure](Project-Structure)) and
fails on a broken rule.

## Installers and the Autodesk bundle

`pack` writes to `output/`:

| File | What it is |
| --- | --- |
| `<product>-<version>-SingleUser.msi` | installs for the current user, no administrator rights |
| `<product>-<version>-MultiUser.msi` | installs for everyone on the machine (administrator rights) |
| `<product>.bundle.zip` | the package for the Autodesk App Store |

Every installer covers every Revit version. The product name, the vendor and the installer's UpgradeCode are in
`build/appsettings.json` — keep the UpgradeCode: a new version replaces the old one by it.

The first `pack` installs WiX 7 and accepts its EULA (Open Source Maintenance Fee) with `wix eula accept wix7`.

## Versions

The version comes from git through GitVersion: a tag `v1.2.0` on the commit gives `1.2.0`, other commits a prerelease
version. `Version` in `build/appsettings.json` overrides it. Tag a release before `pack`.

## CI

`.github/workflows/Compile.yml` runs the pipeline on every push to `main` and every pull request: the boundary check,
`ClientApp` and every `Release.R2x`.

## Tests

The template does not add test projects to your add-in. Two kinds fit:

* **Without Revit** — the pipeline, middleware, your services: an ordinary test project (TUnit, xUnit) referencing the
  add-in or a module.
* **Inside Revit** — code that needs the Revit API: [Nice3point.TUnit.Revit](https://www.nuget.org/packages/Nice3point.TUnit.Revit)
  starts the Revit of the configuration and runs the tests on its thread. Revit must be installed and closed.
