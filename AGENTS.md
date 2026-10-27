# Davydov.Revit.Template

Этот репозиторий — шаблон `dotnet new davydov-revit` и одновременно рабочее решение: плагин-заглушка
`Davydov.Revit.Addin`, который собирается, тестируется и проходит CI. Решения и их история — [docs/PLAN.md](docs/PLAN.md),
проверки в живом Revit — [docs/manual-tests.md](docs/manual-tests.md).

Этот файл — для работы над шаблоном. Плагин, созданный из шаблона, получает свои `AGENTS.md`, `CLAUDE.md`,
`ARCHITECTURE.md`, навыки `.agents/skills/` и `README.md` из `template/content/`. Правила кода плагина — там же,
в `template/content/AGENTS.md`: они действуют и здесь.

## Что уходит в шаблон, а что нет

Корень репозитория — содержимое шаблона. Только для этого репозитория, и как это вырезается:

| Что | Как |
| --- | --- |
| `tests/`, `docs/`, `wiki/`, `template/`, `LICENSE`, `AGENTS.md`, `CLAUDE.md`, `README.md`, `.github/workflows/Template.yml`, `PublishTemplate.yml` и `Wiki.yml`, `build/RepositoryModules.cs`, `build/Modules/Test*` | исключения в `sources` файла `.template.config/template.json` |
| папки `Docs`, `Tests` и пакет шаблона в `.slnx` | блоки `<!--#if (false)-->` … `<!--#endif-->` |
| тестовые модули пайплайна | partial-метод `AddRepositoryModules` в `build/Program.cs`, реализация — в исключённом `build/RepositoryModules.cs`; без неё вызов исчезает |
| `-- test` в `Compile.yml` | символ шаблона `pipelineRun` заменяет строку |
| README, `AGENTS.md`, `CLAUDE.md`, `ARCHITECTURE.md`, навыки плагина | лежат в `template/content/`, второй источник шаблона копирует их в корень проекта |
| классический `.sln` для Visual Studio | `template/content/Davydov.Revit.Addin.sln` — **генерируется** из `.slnx` модулем `TemplateSolutionModule` (только репозиторий): локально пайплайн перезаписывает его, CI падает, если он отстал. Руками не править; новый блок «только для репозитория» в `.slnx` — добавить и в этот модуль |

Новый файл только для репозитория — сразу в исключения. Тесты в шаблон не входят.

## Имена и идентификаторы

* `sourceName` — `Davydov.Revit.Addin`. Шаблонизатор заменяет его как есть и в формах `Davydov_Revit_Addin` (имена
  Sourcy в `build/`) и `davydov-revit-addin` (символ `kebabName`: `package.json`, virtual host). Других форм он не знает:
  регекс с экранированными точками, `DAVYDOV_REVIT_ADDIN` и т. п. останутся как есть. Имя пишется литералом, а
  производное строится в коде (пример — `$addinName` и `[regex]::Escape` в `build/Check-Boundaries.ps1`).
* Модули — часть плагина (решение 38): `Davydov.Revit.Hosting` → `<имя>.Hosting` (символ `modulesName`, переименовывает
  и папки), `Davydov_Revit_Hosting` → `<имя>_Hosting` (`modulesIdentifier`). Ключи `AppContext` Launcher → модуль
  начинаются с `Davydov.Revit.Addin.Launcher`, их заменяет `sourceName`. В созданном проекте с `--vendor` не остаётся ни
  одного «Davydov» — это проверяет `Template.yml`.
* Производитель — параметр `--vendor`: заменяет `<VendorId>Davydov</VendorId>` и `"Name": "Davydov"` в `build/appsettings.json`.
* GUID-ы, которые у каждого плагина свои, — в `guids` файла `template.json`: `AddInId` в `.addin`, GUID док-панели в
  `Host.cs`, UpgradeCode в `build/appsettings.json`, id сервера DirectContext3D в `OverlayServer.cs`. Новый такой GUID — туда же.
* `#if` в C# шаблонизатор не обрабатывает (`"*.cs": { "operations": [] }`), поэтому `#if DEBUG` безопасен, а условий
  шаблона в `.cs` нет.

## Решение: `.slnx` и `.sln`

Окно «Новый проект» Visual Studio не открывает `.slnx` шаблона, а сохраняет поверх него своё решение — без папок и с
конфигурациями `Debug`/`Release` (решение 39). Поэтому параметр `--solution-format`: `auto` (по умолчанию) — `.sln` в
Visual Studio (`HostIdentifier == "vs"`), `.slnx` везде ещё; `slnx` и `sln` — явно. `Template.yml` собирает оба варианта:
проект по умолчанию (`.slnx`) и проект с `--solution-format sln`.

## Код и документы

* В коде, который уходит в шаблон, нет ссылок на `docs/PLAN.md` («plan, section N», «decision N») и на AnalyseTool:
  в плагине их нет. Если ссылка объясняла причину — пишется сама причина. `Template.yml` это проверяет.
* Файлы для автора плагина (`template/content/`, комментарии и сообщения в коде) и README репозитория — на
  английском. Рабочие документы репозитория (PLAN.md, manual-tests.md, этот файл) — на русском.
* Имена классов, методов и файлов в документах и навыках сверяются с кодом.
* Вики для авторов плагинов — в `wiki/` (английский, примеры на `Acme.Revit.Tools`), правится через PR вместе с кодом;
  `Wiki.yml` копирует её в GitHub Wiki при пуше в `main`, правки прямо в GitHub Wiki затираются. Изменилось поведение,
  которое описывает вики, — страница меняется в том же PR.
* Решение записывается в раздел «Решения» PLAN.md; заменённое — зачёркивается со ссылкой на новое.

## Проверки

* `dotnet run -c Release --project build -- test` — проверка границ, `ClientApp`, компиляция R25–R27, `Hosting.Tests`,
  Vitest в `revit-bridge`; тесты внутри Revit — только локально и при закрытом Revit (с `CI=true` пропускаются).
* `dotnet run -c Release --project build -- pack` — MSI и bundle в `output/`.
* `.github/workflows/Compile.yml` и `Template.yml` обязаны проходить; `Template.yml` вызывается и из
  `PublishTemplate.yml` перед публикацией (`workflow_call`). `Template.yml` собирает пакет шаблона, создаёт
  `Acme.Revit.Tools`, проверяет, что в нём нет заглушек и файлов репозитория, собирает его и проверяет правило Launcher.
* Локально пакет шаблона: `dotnet pack template/Davydov.Revit.Templates.csproj -o artifacts`, затем
  `dotnet new install` и `dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme` во временной папке. Перед
  повторной установкой — `dotnet new uninstall Davydov.Revit.Templates`, иначе установятся две копии.

## Выпуск шаблона

Пакет `Davydov.Revit.Templates` публикуется на nuget.org через `.github/workflows/PublishTemplate.yml` (Trusted
Publishing: политика на nuget.org указывает на этот файл, окружение GitHub `nuget` и пользователя `NikolaiDavydov`;
ключей в секретах нет). Имя файла и окружение менять только вместе с политикой на nuget.org.

* Публикация — тег `v<версия>` на `main`; ручной запуск с dry run ничего не публикует.
* Версию на nuget.org нельзя удалить, только скрыть: тег ставится только по прямому запросу владельца.
* README страницы пакета — `template/README.md`; README созданного проекта — `template/content/README.md`.
