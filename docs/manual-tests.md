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

## Фаза 1 — скелет

Для каждой версии Revit 2025, 2026, 2027:

| # | Шаг | Ожидается |
| --- | --- | --- |
| 1 | Запустить Revit | На вкладке «Davydov.Revit.Addin» — панель «General» с кнопкой «Status» (в Debug — ещё панель «Dev») |
| 2 | Нажать «Status» | Окно со сведениями: `Load context: Davydov.Revit.Addin` (не `Default`), Microsoft.Extensions.\* версии 10.x в том же контексте |
| 3 | Открыть журнал Revit (`%LocalAppData%\Autodesk\Revit\Autodesk Revit <год>\Journals`, последний файл) | Строка `'Davydov.Revit.Addin_Information { … Status requested … }` (в Debug журнал пишет от `Information`) |
| 4 | Без открытого документа посмотреть на кнопку «Active document» (Debug) | Кнопка неактивна |
| 5 | Открыть или создать проект, нажать «Active document» | Окно с названием документа |
| 6 | Рядом с AnalyseTool: нажать «Status», затем пользоваться AnalyseTool | Оба плагина работают; у нашего — по-прежнему свой контекст и свои версии зависимостей |
| 7 | Сломать старт: временно бросить исключение в `Host.StartAsync`, собрать, запустить Revit | На вкладке «Add-Ins» — панель «Davydov.Revit.Addin: startup error», кнопка открывает `%LOCALAPPDATA%\Davydov.Revit.Addin\logs\<год>\startup-error.txt` с текстом исключения |
| 8 | Удалить строку `StatusCommand` из `Launcher/Proxies.cs`, собрать, запустить Revit | Запасная панель; в логе — «The Launcher has no proxy for … Add this line to Proxies.cs …» |
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
