# Davydov.Revit.Template

Стартовый шаблон Revit-плагинов (Revit 2025–2027) с WebView2-интерфейсом. Основа — структура и инструменты
[Nice3point Revit Templates](https://github.com/Nice3point/RevitTemplates) (MIT): Revit SDK, Toolkit, Extensions,
`Host` + `ServiceDefaults`. Поверх — свои модули из опыта AnalyseTool. Подробности и решения — [docs/PLAN.md](docs/PLAN.md).

`Davydov.Revit.Addin` — имя-заглушка: при создании проекта из шаблона оно заменяется на имя нового плагина.

## Структура

| Проект | Что это |
| --- | --- |
| `source/Davydov.Revit.Addin.Launcher` | единственная сборка, которую грузит Revit: `.addin`, свой `AssemblyLoadContext`, прокси кнопок |
| `source/Davydov.Revit.Addin` | приложение: `Application`, `Host`, кнопки (`Commands/`) и обвязка (`Infrastructure/`: связь кнопок с Launcher, диагностика Debug) |
| `source/Davydov.Revit.Addin.ServiceDefaults` | логирование (журнал Revit), диагностика, JSON, проверка DI |
| `source/Davydov.Revit.Hosting` | наш модуль: конвейер команд, `IRevitDispatcher`, жизненный цикл Revit, настройки |

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

Каждый запуск пайплайна проверяет границы проектов (`build/Check-Boundaries.ps1`, раздел 3.1 плана): кто на что
ссылается, без WPF в `Davydov.Revit.Hosting`, без `ActiveUIDocument` в `Features/` и `Services/` приложения, без типов
приложения в Launcher. Отдельно: `pwsh build/Check-Boundaries.ps1`.

Установщики и пакет для Autodesk App Store — в `output/`:

```
dotnet run -c Release --project build -- pack
```

`…-SingleUser.msi` ставит плагин текущему пользователю, `…-MultiUser.msi` — всем (нужны права администратора),
`<продукт>.bundle.zip` — для Autodesk App Store. Сборку начинает очистка всех `bin/` и `obj/`. При первом запуске
ставится WiX 7, его EULA (Open Source Maintenance Fee) принимается командой `wix eula accept wix7`. Имя продукта,
UpgradeCode и производитель — в `build/appsettings.json`.

## Шаблон

Этот репозиторий и есть шаблон `dotnet new davydov-revit`: `.template.config/template.json` и пакет
`template/Davydov.Revit.Templates.csproj`. В новый проект не попадают тесты, `docs/`, `template/`, `LICENSE` и
`Template.yml`; имена `Davydov.Revit.Addin` и GUID-ы меняются, README — `template/content/README.md`. Локально:

```
dotnet pack template/Davydov.Revit.Templates.csproj -o artifacts
dotnet new install artifacts/Davydov.Revit.Templates.0.1.0.nupkg
dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme
```

`.github/workflows/Template.yml` на каждом PR создаёт проект из шаблона и собирает его.

Проверки в живом Revit — [docs/manual-tests.md](docs/manual-tests.md).
