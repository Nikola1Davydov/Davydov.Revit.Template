# Davydov.Revit.Template

Стартовый шаблон Revit-плагинов (Revit 2025–2027) с WebView2-интерфейсом. Основа — структура и инструменты
[Nice3point Revit Templates](https://github.com/Nice3point/RevitTemplates) (MIT): Revit SDK, Toolkit, Extensions,
`Host` + `ServiceDefaults`. Поверх — свои модули из опыта AnalyseTool. Подробности и решения — [docs/PLAN.md](docs/PLAN.md).

`Davydov.Revit.Addin` — имя-заглушка: при создании проекта из шаблона оно заменяется на имя нового плагина.

## Структура

| Проект | Что это |
| --- | --- |
| `source/Davydov.Revit.Addin.Launcher` | единственная сборка, которую грузит Revit: `.addin`, свой `AssemblyLoadContext`, прокси кнопок |
| `source/Davydov.Revit.Addin` | приложение: `Application`, `Host`, кнопки (`Commands/`) |
| `source/Davydov.Revit.Addin.ServiceDefaults` | логирование (журнал Revit), диагностика, JSON, проверка DI |
| `source/Davydov.Revit.Hosting` | наш модуль: кнопки ленты через Launcher (дальше — конвейер команд, документы, события) |

## Сборка

Нужен .NET SDK 10. Конфигурации — `Debug.R25` … `Release.R27`:

```
dotnet build Davydov.Revit.Addin.slnx -c Debug.R26
```

Debug-сборка сама раскладывает плагин в `%AppData%\Autodesk\Revit\Addins\<год>\`; Release — только в
`source/Davydov.Revit.Addin.Launcher/bin/Release.R2x/publish/`.

Если сборка падает с «workload set … has missing manifests», это поломка установки .NET SDK, а не проекта: либо
`dotnet workload repair`, либо собирать с переменной окружения `MSBuildEnableWorkloadResolver=false`.

Вся сборка — пайплайн ModularPipelines в `build/` (как у Nice3point): `ClientApp` и все `Release.R2x`:

```
dotnet run -c Release --project build
```

С тестами (`Davydov.Revit.Hosting.Tests`, Vitest в `revit-bridge` и тесты внутри Revit — для них Revit должен быть
закрыт, он запускается сам):

```
dotnet run -c Release --project build -- test
```

В CI (`.github/workflows/Compile.yml`, на `push` в `main` и на PR) — то же с `test`, но без тестов внутри Revit.

Проверки в живом Revit — [docs/manual-tests.md](docs/manual-tests.md).
