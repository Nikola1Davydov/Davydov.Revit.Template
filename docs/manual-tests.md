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
| 2 | Открыть `%AppData%\Autodesk\Revit\Addins6` | `Davydov.Revit.Addin.Launcher.addin` и папка `Davydov.Revit.Addin.Launcher` с `ClientApp\dist` |
| 3 | Запустить Revit 2026, нажать «Sample page» | Страница открывается из `dist` (Release, без dev-сервера) |
| 4 | Собрать ещё раз (`pack`) и поставить новый MSI поверх | В «Программах и компонентах» одна запись, не две |
| 5 | Удалить через «Программы и компоненты» | Папки и `.addin` в `Addins\<год>` удалены |
| 6 | `…-MultiUser.msi` (от администратора) | 2025, 2026 — в `%ProgramData%\Autodesk\Revit\Addins`, 2027 — в `%ProgramFiles%\Autodesk\Revit\Addins` |

## Шаблон (фаза 6)

`dotnet pack template/Davydov.Revit.Templates.csproj -o <папка>`, затем `dotnet new install <папка>\Davydov.Revit.Templates.<версия>.nupkg`:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | `dotnet new davydov-revit -n Acme.Revit.Tools --vendor Acme`, `git init`, коммит, `dotnet run -c Release --project build -- pack` | Сборка, `Acme.Revit.Tools-…-SingleUser.msi`, `…-MultiUser.msi`, `Acme.Revit.Tools.bundle.zip` |
| 2 | Создать второй плагин (`-n Contoso.Revit.Tools`), собрать оба в `Debug.R26` | В `Addins6` два `.addin` и две папки Launcher |
| 3 | Запустить Revit 2026 | Две вкладки на ленте, кнопки обоих работают; «Status» каждого — свой `Load context` (`Acme.Revit.Tools`, `Contoso.Revit.Tools`) |
| 4 | «Sample page» в каждом | Свои окна; логи и `usersettings` — в своих папках `%LOCALAPPDATA%` и `%AppData%` |
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
| 1 | Запустить Revit, открыть журнал | Строка `Registered 4 commands: CheckActiveDocument, CountElementsByCategory, GetActiveDocument, Host.GetCommands` |
| 2 | Открыть проект, нажать «Active document» (панель Dev), ничего не трогать 5 с | Окно: название, ключ (путь файла или заголовок несохранённого), число элементов; в журнале — `Command CheckActiveDocument (ribbon) succeeded in ~5000 ms` |
| 3 | Открыть два проекта; нажать «Active document» и сразу переключиться на другой проект | Окно ошибки «The active document has changed. Refresh the data and try again.»; в журнале — `refused: document_changed` |
| 4 | Нажать «Active document» и сразу закрыть все документы | Окно ошибки «Open a document first.» (`no_document`) |
| 5 | Нажать «Active document» и, не дожидаясь, пользоваться Revit | Revit не подвисает: команда ждёт в thread pool, в поток Revit заходит только на чтение |
| 6 | Закрыть Revit; `dotnet test --project tests/Davydov.Revit.Addin.Tests -c Release.R26` (и R25, R27) | Revit запускается сам, все тесты проходят (`RevitDispatcherTests`, `ActiveDocumentTests`, `DocumentKeyTests`) |

## Фаза 4 — WebView

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

