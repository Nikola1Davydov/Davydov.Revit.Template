# Ручные проверки

Что автоматические тесты не покрывают: путь запуска плагина в живом Revit (`.addin` → Launcher → свой ALC → `Host` →
лента) и соседство с другими плагинами. Проверять перед релизом и после изменений в Launcher, ServiceDefaults или
версиях пакетов.

## Подготовка

1. Собрать Debug-конфигурацию нужной версии — она сама разложит плагин в `%AppData%\Autodesk\Revit\Addins\<год>\`:

   ```
   dotnet build Davydov.Revit.Addin.slnx -c Debug.R25
   ```

   (`Debug.R26`, `Debug.R27` — для других версий.) Release-сборки только публикуют в
   `source/Davydov.Revit.Addin.Launcher/bin/Release.R2x/publish/`.
2. Для проверки соседства в той же папке `Addins\<год>\` должен лежать AnalyseTool (или другой плагин со своими
   версиями Microsoft.Extensions.\*).

## Установщик (фаза 5)

`dotnet run -c Release --project build -- pack`, затем на машине **без Debug-сборки плагина** (установщик пишет в ту же
папку `Addins\<год>`, куда её раскладывает `Debug.R2x`, а удаление её сотрёт):

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить `output\…-SingleUser.msi` | Дерево компонентов с Revit 2025, 2026, 2027; установка без прав администратора |
| 2 | Открыть `%AppData%\Autodesk\Revit\Addins\2026` | `Davydov.Revit.Addin.Launcher.addin` и папка `Davydov.Revit.Addin.Launcher` с `ClientApp\dist` |
| 3 | Запустить Revit 2026, открыть док-панель плагина («Вид» → «Интерфейс» → «Davydov.Revit.Addin») | Страница открывается из `dist` (Release, без dev-сервера) |
| 4 | Собрать ещё раз (`pack`) и поставить новый MSI поверх | В «Программах и компонентах» одна запись, не две |
| 5 | Удалить через «Программы и компоненты» | Папки и `.addin` в `Addins\<год>` удалены |
| 6 | `…-MultiUser.msi` (от администратора) | 2025, 2026 — в `%ProgramData%\Autodesk\Revit\Addins`, 2027 — в `%ProgramFiles%\Autodesk\Revit\Addins` |

## Шаблон (фаза 6)

`dotnet pack template/Davydov.Revit.Templates.csproj -o <папка>`, затем `dotnet new install <папка>\Davydov.Revit.Templates.<версия>.nupkg`:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | `dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme`, `git init`, коммит, `dotnet run -c Release --project build -- pack` | Сборка, `Acme.Revit.Tools-…-SingleUser.msi`, `…-MultiUser.msi`, `Acme.Revit.Tools.bundle.zip` |
| 2 | Создать второй плагин (`-n Contoso.Revit.Tools`), собрать оба в `Debug.R26` | В `Addins\2026` два `.addin` и две папки Launcher |
| 3 | Запустить Revit 2026 | Две вкладки на ленте, кнопки обоих работают; «Status» каждого — свой `Load context` (`Acme.Revit.Tools`, `Contoso.Revit.Tools`) |
| 4 | «Status» в каждом, затем док-панель каждого | Свои окна и панели; логи и `usersettings` — в своих папках `%LOCALAPPDATA%` и `%AppData%` |
| 5 | `dotnet new uninstall Davydov.Revit.Templates` | — |

## Фаза 1 — скелет

Для каждой версии Revit 2025, 2026, 2027:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit | На вкладке «Davydov.Revit.Addin» — панель «General» с кнопкой «Status» (в Debug — ещё панель «Dev») |
| 2 | Нажать «Status» | Окно со сведениями: `Load context: Davydov.Revit.Addin` (не `Default`), Microsoft.Extensions.\* версии 10.x в том же контексте |
| 3 | Открыть журнал Revit (`%LocalAppData%\Autodesk\Revit\Autodesk Revit <год>\Journals`, последний файл) | Строка `'Davydov.Revit.Addin_Information { … Status requested … }` (в Debug журнал пишет от `Information`) |
| 4 | Без открытого документа посмотреть на кнопку «Active document» (Debug) | Кнопка неактивна |
| 5 | Открыть или создать проект, нажать «Active document» | Через ~5 с — окно с названием документа (с фазы 3 кнопка идёт через конвейер, см. ниже) |
| 6 | Рядом с AnalyseTool: нажать «Status», затем пользоваться AnalyseTool | Оба плагина работают; у нашего — по-прежнему свой контекст и свои версии зависимостей |
| 7 | Сломать старт: временно бросить исключение в `Host.StartAsync`, собрать, запустить Revit | Окно Revit об ошибке внешнего приложения с текстом исключения; полный текст — в `%LOCALAPPDATA%\Davydov.Revit.Addin\logs\<год>\startup-error.txt` |
| 8 | Удалить строку `StatusCommand` из `Launcher/Proxies.cs`, собрать, запустить Revit | Окно Revit об ошибке; в `startup-error.txt` — «The Launcher has no proxy for … Add this line to Proxies.cs …» |
| 9 | Отладка: запустить Launcher-проект из IDE (`LaunchRevit`), поставить точку останова в `StatusCommand.Execute`, нажать кнопку | Остановка в точке; проверить, работают ли Hot Reload / Edit and Continue в изолированном контексте — записать результат в CLAUDE.md шаблона |

После шагов 7–8 вернуть код обратно.

## Фаза 2 — основа модуля Davydov.Revit.Hosting

Debug-сборка, для каждой версии Revit 2025, 2026, 2027:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit | Revit запускается без задержки и зависания (в Debug при старте работает `StartupProbe` с обычными `await`) |
| 2 | Открыть журнал Revit | Строки `Host started in N ms` (записать N для каждой версии) и `Revit <год> initialized; open documents: 0` |
| 3 | Нажать «Status» | `Revit initialized: True`, путь `User settings: %AppData%\Davydov.Revit.Addin\<год>\usersettings.json` |
| 4 | Нажать «User settings» (панель Dev) 2–3 раза | Окно `Clicks: 1`, `2`, `3`; в `usersettings.json` — `"Dev": { "ClickCount": 3 }` |
| 5 | Перезапустить Revit, нажать «User settings» | `Clicks: 4` — настройка пережила перезапуск |
| 6 | Положить рядом с плагином `appsettings.json` с `{ "Dev": { "ClickCount": 100 } }`, удалить `usersettings.json`, перезапустить, нажать «User settings» | `Clicks: 101` — значение из `appsettings.json`, пользовательский файл поверх него |
| 7 | Открыть `%LOCALAPPDATA%\Davydov.Revit.Addin\logs\<год>\log-<дата>.txt` | Те же записи, что в журнале, от `Information`, с категорией (`Davydov.Revit.Addin.Application: Host started in N ms`); в Release журнал — от `Warning`, а файл — по-прежнему от `Information` |

## Фаза 3 — команды и конвейер

Debug-сборка, для каждой версии Revit 2025, 2026, 2027:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit, открыть журнал | Строка `Registered 5 commands: CheckActiveDocument, CountElementsByCategory, GetActiveDocument, GetLogEntries, Host.GetCommands` (в Release — 3, без Debug-команд) |
| 2 | Открыть проект, нажать «Active document» (панель Dev), ничего не трогать 5 с | Окно: название, ключ (путь файла или заголовок несохранённого), число элементов; в журнале — `Command CheckActiveDocument (ribbon) succeeded in ~5000 ms` |
| 3 | Открыть два проекта; нажать «Active document» и сразу переключиться на другой проект | Окно ошибки «The active document has changed. Refresh the data and try again.»; в журнале — `refused: document_changed` |
| 4 | Нажать «Active document» и сразу закрыть все документы | Окно ошибки «Open a document first.» (`no_document`) |
| 5 | Нажать «Active document» и, не дожидаясь, пользоваться Revit | Revit не подвисает: команда ждёт в thread pool, в поток Revit заходит только на чтение |
| 6 | Закрыть Revit; `dotnet test --project tests/Davydov.Revit.Addin.Tests -c Release.R26` (и R25, R27) | Revit запускается сам, все тесты проходят (`RevitDispatcherTests`, `ActiveDocumentTests`, `DocumentKeyTests`) |

## Фаза 4 — WebView

С решения 43 на ленте нет кнопок «Sample page» и «Palette». Для шагов с окном временно добавьте кнопку с
`Host.GetService<IWindows>().Open("/")` (навык `add-ribbon-button`); док-панель с маршрутом `/dock` открывает
«Вид» → «Интерфейс» или кнопка «Dev page» (Debug, маршрут `/dev`).

Подготовка: `npm install` в `source/Davydov.Revit.Hosting.WebView/revit-bridge` и в `source/Davydov.Revit.Addin/ClientApp`.
Debug-сборка грузит dev-сервер: перед запуском Revit — `npm run dev` в `ClientApp`. Для проверки Release-варианта —
`npm run build` и в `appsettings.Development.json` `"ClientApp": { "UseDevServer": false }`.

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Открыть проект, нажать «Sample page» | Окно поверх Revit со страницей: название документа, кнопка «Count elements» |
| 2 | Нажать «Sample page» ещё раз | То же окно выходит на передний план, второго окна нет |
| 3 | Нажать «Count elements» в большом проекте | Полоса прогресса с «N / M», затем таблица категорий; Revit во время подсчёта отвечает |
| 4 | Нажать «Count elements» и сразу «Cancel» | Подсчёт останавливается, в журнале одна строка `canceled by the caller`, без `WARNING` |
| 5 | Открыть второй проект, переключиться на него, нажать «Count elements» в окне | Ответ `document_changed`, страница сама показывает название нового документа; повторное нажатие считает его |
| 6 | Нажать «Palette» | Док-панель рядом с браузером проекта, страница с заголовком «Palette»; перезапуск Revit — панель на месте |
| 7 | Закрыть окно посреди подсчёта | Revit не падает; в журнале нет ошибок отправки |
| 8 | В Debug: F12 в окне | Открываются DevTools; в Release DevTools и контекстное меню выключены |
| 9 | В консоли DevTools: `location.href = "https://example.com"` | Переход отменён, адрес открылся в системном браузере (защита навигации включена по умолчанию). С `w.AllowAnyNavigation()` в `Host.cs` — сайт открывается в окне |
| 10 | Удалить WebView2 Runtime (или проверить на машине без него) и открыть «Sample page» | В окне сообщение со ссылкой на установку вместо пустого окна |

## Dev-страница (Debug)

Кнопка «Dev page» на панели «Dev» показывает в док-панели страницу `/dev`: живой лог плагина и форму запуска любой
команды. Панель одна, поэтому страница занимает место маршрута `/dock` до перезапуска Revit.

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Нажать «Dev page», вкладка «Logs» | В панели — записи с момента старта: `Host started in N ms`, `Registered 5 commands: …`, `Revit <год> initialized`; записи самого опроса (`Command GetLogEntries`) в списке нет, а в журнале и файле — есть |
| 2 | Нажать «Status» | Через полсекунды в списке `Status requested: …`; «Follow» прокручивает к последней записи |
| 3 | «Pause», нажать «Status», «Resume» | Пока пауза — ничего не приходит; после «Resume» — пропущенная запись появляется |
| 4 | Выбрать `Warning+`, нажать «Active document» без документа | Кнопка неактивна; открыть документ, нажать «Active document» и сразу закрыть его — в списке `refused: no_document` или `document_changed` уровня `WRN` |
| 5 | Вкладка «Commands», выбрать `CountElementsByCategory`, «Run» | Прогресс, затем JSON с категориями и `Succeeded in N ms`; «Cancel» посреди подсчёта — `aborted` |
| 6 | Выбрать `GetLogEntries`, в поле `{ "afterSequence": 0 }`, «Run» | JSON со всеми записями буфера (не больше 1000) |
| 7 | В поле `{ "afterSequence": "x" }`, «Run» | `bad_request` с сообщением десериализации; с пустым полем — `bad_request: … expects a payload` |
| 8 | Открыть второй документ, снять флажок «Document header», «Run» `CountElementsByCategory`, переключиться на другой документ во время подсчёта | Команда дорабатывает по документу, на котором началась; с флажком — `document_changed` |
| 9 | Release-сборка (`"ClientApp": { "UseDevServer": false }`, `npm run build`), открыть `#/dev` через DevTools-адрес или временную кнопку | Вкладка «Logs» пишет «The log is available in a Debug build only.», вкладка «Commands» работает |

## MCP (прототип, решение 41)

Debug-сборка: сервер включён. Порт — базовый из `Host.cs` (`AddMcp`) + (год − 2025).

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit 2026 | В журнале `MCP server listens on http://localhost:<порт>/mcp`; появился `%LOCALAPPDATA%\Davydov.Revit.Addin\2026\mcp.json` с `url`, `token`, `claudeCode` |
| 2 | Выполнить команду из `claudeCode` в терминале, затем `claude mcp list` | Сервер `davydov-revit-addin-2026` — `Connected` |
| 3 | В Claude Code: «какие инструменты у revit?» | `GetActiveDocument`, `CountElementsByCategory`; ни `CheckActiveDocument`, ни `Host.*` |
| 4 | Без открытого документа попросить вызвать `GetActiveDocument` | Ответ `null`; `CountElementsByCategory` — ошибка `no_document: …` |
| 5 | Открыть большой проект, попросить посчитать элементы по категориям | Таблица категорий; Revit отвечает во время подсчёта; в журнале `Command CountElementsByCategory (mcp) succeeded` |
| 6 | Перезапустить Revit, снова вызвать инструмент без изменения конфигурации клиента | Работает: токен в `mcp.json` тот же |
| 7 | Запустить Revit 2025 рядом | Свой порт (на 1 меньше), свой `mcp.json` в `…\2025\` |
| 8 | Запустить второй Revit 2026 | В журнале второго — предупреждение о занятом порте, плагин работает |
| 9 | `curl -i -X POST http://localhost:<порт>/mcp` без заголовков | `401` |
| 10 | Release-сборка без `Mcp:Enabled` | В журнале `MCP server is off`, порт не слушается |
| 11 | «Dev page» → вкладка «MCP» | `listening` и адрес; команда для Claude Code и JSON с токеном, скрытым до последних 4 символов; «Copy» кладёт в буфер полный текст — его можно сразу выполнить в терминале (шаг 2); список инструментов |
| 12 | Второй Revit той же версии → «Dev page» → «MCP» | `port busy` и подсказка; токена нет |

## Выполнение C# (решение 42)

Debug-сборка: `ExecuteCode` включена. В Claude Code (сервер подключён, раздел MCP) после перезапуска Revit — переподключить сервер через `/mcp`: он заново читает список инструментов.

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit | Плагин стартует: Roslyn грузится в ALC плагина без ошибок; в `/mcp` есть `ExecuteCode` |
| 2 | «Выполни в Revit: верни название документа и число стен» | Агент вызывает `ExecuteCode`, ответ с названием и числом; в журнале `Command ExecuteCode (mcp) succeeded` |
| 3 | Код с опечаткой | `compile_failed` со строкой `script.cs(N,…)` по строкам присланного кода |
| 4 | `throw new InvalidOperationException("x");` в третьей строке | `script_failed: InvalidOperationException: x (script.cs:line 3)` |
| 5 | `TaskDialog.Show("Script", doc.Title);` | Окно в Revit; после закрытия — результат `null` |
| 6 | Код с `Transaction`, который переименовывает уровень | Уровень переименован, отмена (Ctrl+Z) возвращает имя |
| 7 | Вернуть `doc.ActiveView` (объект Revit) | Ответ — JSON или текст типа, Revit не падает |
| 8 | Вкладка «Commands» Dev-страницы, `ExecuteCode`, `{ "code": "return 1 + 1;" }` | `2` |
| 9 | Release-сборка без `Scripting:Enabled` | Команды `ExecuteCode` нет ни в `/mcp`, ни в «Commands» |
| 10 | Двадцать вызовов подряд | Память Revit не растёт заметно: сборки скриптов выгружаются |

## Инструменты для агента (решение 44)

Debug-сборка, проект открыт; в Claude Code после перезапуска Revit — переподключить сервер через `/mcp`.

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | `/mcp` | Есть `RenderView`, `GetSelection`, `SelectElements`, `GetLogEntries`, `DescribeApi` |
| 2 | «Покажи, что на виде» | `RenderView` — картинка вида в ответе агента; в `%TEMP%` не остаётся папок `…-render-…` |
| 3 | Открыть спецификацию, `RenderView` | `view_not_exportable` |
| 4 | Выделить несколько элементов, «что выделено?» | `GetSelection`: число и id, категория, имя каждого; с `limit` 1 — один элемент, число — полное |
| 5 | «Выдели все двери» | `SelectElements`: двери выделены, вид приближен; модель не изменилась (Ctrl+Z нечего отменять) |
| 6 | «Что в логе плагина за последнее время, только предупреждения?» | `GetLogEntries` с `minLevel` `Warning` |
| 7 | «Опиши тип Wall» / `DescribeApi` с `name` `ElementId` | Члены с сигнатурами и текстом из документации; у `ElementId` в 2025 есть `IntegerValue`, в 2026 и 2027 — нет (поэтому и нужен `DescribeApi`); первый вызов — до пары секунд (чтение `RevitAPI.xml`) |
| 8 | `DescribeApi` с `name` `Collector` | Список подходящих имён, в том числе `FilteredElementCollector` |
