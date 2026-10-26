# Davydov.Revit.Template — план

Стартовый проект для новых Revit-плагинов. Основа — структура и инструменты **Nice3point** (Revit Templates, SDK,
Toolkit, ServiceDefaults, ModularPipelines, WixSharp, TUnit). Поверх неё — **наши модули**, перенесённые из опыта
AnalyseTool: WebView2-интерфейс и конвейер команд в стиле ASP.NET Core.

> Статус: черновик, обсуждается. Решения фиксируются в разделе «Решения», выбор, который ещё не сделан, —
> в «Открытых вопросах» (буквы — ссылки на них по тексту). Эталон структуры — `samples/MultiProjectSolution`
> в [Nice3point/RevitTemplates](https://github.com/Nice3point/RevitTemplates).

## 1. Идея

Две части (решение 22):

| Часть | Откуда | Что даёт |
| --- | --- | --- |
| **Основа** | Nice3point | `Nice3point.Revit.Sdk` (конфигурации `Debug.R25…Release.R27`, TFM, `DeployAddin`, `LaunchRevit`), `Nice3point.Revit.Toolkit` (external events, док-панели, базовые классы приложения и команд), `Nice3point.Revit.Extensions` (лента), `Host.cs` + `ServiceDefaults` (Generic Host, логирование в журнал Revit), ModularPipelines (сборка), WixSharp (установщик), TUnit + `Nice3point.TUnit.Revit` (тесты) |
| **Наши модули** | AnalyseTool + этот план | `Davydov.Revit.Hosting` — конвейер команд, `IRevitDispatcher`, жизненный цикл Revit, настройки (правило документа и кнопки ленты — в приложении, разделы 7 и 8, решение 35); `Davydov.Revit.Hosting.WebView` — WebView2-окна и док-панель, мост к React; `Davydov.Revit.Addin.Launcher` — изоляция зависимостей на всех версиях Revit (решение 26) |

Наши модули подключаются так же, как у Nice3point подключается `AddServiceDefaults()` — методами-расширениями
`IHostApplicationBuilder`, в стиле ASP.NET Core / .NET Aspire:

| ASP.NET Core | В шаблоне |
| --- | --- |
| `WebApplication.CreateBuilder()` / `Program.cs` | `Host.cs` по образцу Nice3point: `HostApplicationBuilder` с `DisableDefaults = true` |
| `builder.AddServiceDefaults()` (Aspire) | то же: проект `Davydov.Revit.Addin.ServiceDefaults` из шаблона Nice3point |
| `builder.Services` (DI), `IOptions<T>`, `ILogger<T>` | то же, Microsoft.Extensions.* |
| Controllers + `AddControllers()` | классы команд + `builder.AddRevitCommands(...)` (reflection-скан сборок, как в AnalyseTool) |
| Middleware | конвейер команд, настраивается в `AddRevitCommands(c => c.Use…)` (раздел 5.4) |
| Model binding | payload автоматически → `TRequest` |
| Kestrel (`IServer`) | транспорты: WebView2, позже MCP |
| `IHostedService`, `IHostApplicationLifetime` | то же; момент «Revit готов» — свой `IRevitLifetime.Initialized` (раздел 6) |
| `wwwroot` / SPA ClientApp | `ClientApp/` (React + Vite), dev-сервер с HMR в Debug |
| `SynchronizationContext` | `IRevitDispatcher` — единственный путь в Revit API, поверх `AsyncExternalEvent` из Toolkit |

### Как это выглядит

```csharp
// source/Davydov.Revit.Addin/Application.cs — как у Nice3point
public class Application : AsyncExternalApplication          // Toolkit; создаётся Launcher'ом внутри ALC (раздел 8)
{
    public override async Task OnStartupAsync()
    {
        await Host.StartAsync(Application);                    // исключение дойдёт до Launcher → лог и окно ошибки Revit (раздел 8)
        CreateRibbon();
    }

    public override async Task OnShutdownAsync() => await Host.StopAsync();

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Семейства", "Davydov.Revit.Addin");
        panel.AddButton<FamiliesCommand>("Семейства").WithIcons("Families");                       // иконки — встроенные ресурсы
        panel.AddButton<PurgeFamiliesCommand>("Очистить").SetAvailability<ProjectAvailability>();   // наши обёртки, раздел 8
    }
}
```

```csharp
// source/Davydov.Revit.Addin/Host.cs — как у Nice3point + наши модули
public static class Host
{
    private static IHost? _host;

    public static async Task StartAsync(UIControlledApplication application)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Davydov.Revit.Addin",
            ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            DisableDefaults = true,
        });

        builder.AddServiceDefaults();                          // Nice3point: логирование, диагностика, сериализация
        builder.AddRevitHosting(application);                  // наше: диспетчер, жизненный цикл, уведомления — до первого await
        builder.Services.AddScoped<ActiveDocument>();          // правило документа — в приложении (раздел 7)
        builder.AddRevitCommands(c => c
            .FromAssemblyOf<Application>()                     // наше: команды и конвейер (раздел 5)
            .UseCommandLogging()
            .Use<DocumentCheckMiddleware>());
        builder.AddWebView(application, w => w               // наше: WebView2, мост, окна (раздел 9)
            .DockPane(new Guid("…"), "Палитра", "/families-dock"));

        builder.Services.AddSingleton<IFamiliesService, FamiliesService>();

        _host = builder.Build();                               // проверка DI и имён команд — ошибка здесь, а не при клике
        await _host.StartAsync();
    }

    public static Task StopAsync() => _host?.StopAsync() ?? Task.CompletedTask;
    public static T GetService<T>() where T : class => _host!.Services.GetRequiredService<T>();
}
```

```csharp
// source/Davydov.Revit.Addin/Features/Families/RenameFamily.cs — команда конвейера, имя = "RenameFamily"
[Command(Description = "Переименовать семейство")]
public sealed class RenameFamily(ActiveDocument document, ILogger<RenameFamily> log)
    : ICommand<RenameFamilyRequest, RenameFamilyResult>
{
    public Task<RenameFamilyResult> ExecuteAsync(RenameFamilyRequest request, CommandContext context) =>
        document.InvokeAsync(doc => { /* транзакция */ });   // активный документ, зафиксирован на всю команду (раздел 7)
}
```

## 2. Что берём из AnalyseTool, что — у Nice3point, что меняем

| В AnalyseTool | В шаблоне |
| --- | --- |
| Launcher + свой изолированный `AssemblyLoadContext`, вход по FQN-строкам | **остаётся, на всех версиях Revit** (решение 26): Launcher + свой ALC, как в AnalyseTool, но вход — один вызов по имени класса приложения. Штатная изоляция Revit 2026+ (`ManifestSettings`) не используется, у Toolkit своей изоляции больше нет |
| кнопки: рукописный класс в Launcher на кнопку + вызов внутрь по имени-строке; кнопки расширений — AdWindows | **Nice3point:** кнопка — класс `ExternalCommand` в приложении; ставится нашей обёрткой `panel.AddButton<T>()`, которая направляет кнопку на однострочный прокси в Launcher (раздел 8) |
| `AnalyseToolBootstrap` + статический `CoreServices` | **Nice3point:** `Host.cs` (Generic Host) + `AddServiceDefaults()`; наши модули — `builder.AddRevitCommands()` и т.д. |
| свои props, конфигурации `Debug R25` | **Nice3point:** `Nice3point.Revit.Sdk`, конфигурации `Debug.R25…Release.R27`, `REVIT2026_OR_GREATER` |
| NUKE | **Nice3point:** ModularPipelines (`build/`) |
| установщик в `Installer/` | **Nice3point:** WixSharp в `installer/` (+ bundle для Autodesk Store) |
| Serilog в файл | **Nice3point:** ServiceDefaults — Debug + журнал Revit (`Nice3point.Revit.Logging`); **наше:** + файловый лог Serilog (решение 23) |
| `RevitTaskHub` (свой `ExternalEvent` с очередью), второй `ExternalEvent` в `RibbonEventHub` | `IRevitDispatcher` (наш API) **поверх `AsyncExternalEvent` из Toolkit** |
| `RegisterBuiltIns`: reflection-скан `IRevitTask`, конфликт имён — запись в лог | **наше:** тот же reflection-скан `ICommand<…>`, но в DI; ошибки — при `builder.Build()` (раздел 5.3) |
| `Activator.CreateInstance` для команд | **наше:** scope на запрос, внедрение через конструктор |
| `Payload.As<T>()` + `InputType` в атрибуте | **наше:** `ICommand<TRequest,TResponse>` — схема берётся из `TRequest` |
| `CommandRequest.Gate` | **наше:** middleware в конвейере |
| `mcp.json` и свои файлы настроек | `IConfiguration` + `IOptions<T>` + `IUserSettings` для записи (раздел 11) |
| `IProgressAware` (внедрение через свойство) | **наше:** `context.Progress` |
| статический `DocumentTracker`, трюки с `Idling` в `DockPaneHost`, ручной `SendEvent` в док-панели | **наше:** событий в шаблоне пока нет (решение 31); док-панель — `DockablePaneProvider` из Toolkit |
| команды берут `ActiveUIDocument` в момент вызова | **наше:** сервис приложения `ActiveDocument` — активный документ, зафиксированный на всю команду; сменился — понятная ошибка (раздел 7) |
| окна-копии `FamilyControlWindow`, `SettingsWindow`, …; окружение WebView2 в каждом; нет защиты навигации | **наше:** один `WebViewWindow`, одно окружение, защита навигации по желанию (раздел 9) |

Оставляем из AnalyseTool без изменений (проверено в бою):
- Протокол WebView2: Request / Response / Progress с `id` (сообщение Event — когда в шаблон вернутся события, решение 31);
  терпимость к окну, закрытому посреди команды.
- Правило «headless»: модуль `Davydov.Revit.Hosting` без WPF, проверка границ в CI (`Check-Boundaries.ps1`, раздел 3.1).
- Вертикальные срезы: команда, её сервисы и модели лежат в папке своей фичи (`Features/<Срез>/`).

## 3. Структура решения (по образцу Nice3point `MultiProjectSolution`)

```
Davydov.Revit.Addin/
├─ build/                                  ModularPipelines: compile / test / pack (installer + bundle) / publish
│  ├─ Modules/, Options/, Program.cs, appsettings.json
├─ installer/                              WixSharp: .msi per-user и per-machine
├─ source/
│  ├─ Davydov.Revit.Addin/                              приложение (Nice3point «Revit AddIn Application»)
│  │  ├─ Application.cs                    AsyncExternalApplication: Host + лента
│  │  ├─ Host.cs                           Generic Host + AddServiceDefaults + наши модули
│  │  ├─ Commands/                         кнопки ленты — ExternalCommand (как у Nice3point)
│  │  ├─ Infrastructure/                   обвязка, которую обычно не трогают:
│  │  │  ├─ Ribbon/                        AddButton / SetAvailability через прокси Launcher, LauncherInfo, доступность (решение 35)
│  │  │  └─ Diagnostics/                   StartupProbe, DevOptions — только Debug
│  │  ├─ Features/<Срез>/                  команды конвейера (ICommand), их сервисы и модели (как срезы AnalyseTool)
│  │  ├─ Services/Documents/               ActiveDocument + DocumentCheckMiddleware — правило документа (раздел 7)
│  │  ├─ Resources/Icons/
│  │  ├─ appsettings.json
│  │  └─ ClientApp/                        минимальный React + Vite (раздел 10)
│  ├─ Davydov.Revit.Addin.ServiceDefaults/              Nice3point: логирование, диагностика, сериализация (+ наши слои конфигурации)
│  ├─ Davydov.Revit.Hosting/               НАШ модуль, headless: команды, конвейер, IRevitDispatcher, IRevitLifetime, IUserSettings
│  ├─ Davydov.Revit.Hosting.WebView/       НАШ модуль, WPF: окружение WebView2, WebViewWindow, док-панель, транспорт, revit-bridge (TS)
│  └─ Davydov.Revit.Addin.Launcher/                     все версии Revit: Davydov.Revit.Addin.Launcher.addin, App.cs (свой ALC), Proxies.cs, StartupLog.cs (решение 26)
├─ tests/
│  ├─ Davydov.Revit.Addin.Tests/                        TUnit + Nice3point.TUnit.Revit — внутри Revit
│  └─ Davydov.Revit.Hosting.Tests/         TUnit — без Revit
├─ .github/workflows/                      Compile.yml, PublishRelease.yml (из Nice3point)
├─ global.json                             SDK 10, Microsoft.Testing.Platform
├─ Davydov.Revit.Addin.slnx                             конфигурации Debug.R25 … Release.R27
├─ Directory.Packages.props                CPM — наше отклонение от Nice3point (решение 22); CentralPackageFloatingVersionsEnabled=true
├─ CLAUDE.md                               инструкции для ИИ-ассистентов (фаза 6)
└─ .template.config/                       template.json (свой dotnet new, решение 24)
```

Фичи можно выносить в отдельные проекты-модули (Nice3point «Revit AddIn Module»), когда приложение вырастет; по
умолчанию они в `source/Davydov.Revit.Addin/Features/`.

Два слова «команда» в одном проекте: **`Commands/`** — кнопки Revit (`ExternalCommand`), так принято у Nice3point;
**`Features/`** — команды нашего конвейера (`ICommand<…>`), так было в AnalyseTool. В WPF-коде при конфликте имени
`ICommand` с `System.Windows.Input.ICommand` — алиас `using`.

### 3.1. Зависимости проектов (проверяет `Check-Boundaries.ps1` в CI)

| Проект | Ссылается на | Правила |
| --- | --- | --- |
| `Davydov.Revit.Hosting` | Revit API (только компиляция), Nice3point.Revit.Toolkit, Microsoft.Extensions.* | **headless**: без `UseWPF`, `System.Windows`, `MessageBox` |
| `Davydov.Revit.Hosting.WebView` | Hosting, WebView2, Toolkit | единственное место WPF и диалогов наших модулей |
| `Davydov.Revit.Addin.ServiceDefaults` | Toolkit, Nice3point.Revit.Logging, Microsoft.Extensions.Hosting, Serilog (файловый лог, решение 23), Davydov.Revit.Hosting (`AddUserSettings`) | как в Nice3point |
| `Davydov.Revit.Addin.Launcher` | только Revit API; на приложение — **только** ради порядка сборки и `deps.json` | в коде Launcher нельзя использовать типы приложения и модулей: они загрузились бы в default ALC в обход изоляции |
| `Davydov.Revit.Addin` (приложение) | всё перечисленное, Nice3point.Revit.Extensions (лента и обёртки `Infrastructure/Ribbon/`) | в `Features/` и `Services/` — без `System.Windows` (UI — через `IUserNotifier` и фронтенд); документ — через `ActiveDocument`, не `ActiveUIDocument` (раздел 7) |
| `tests/*` | что тестируют | — |

## 4. API — сводно

Короткая спецификация для фаз 2–3; детали — в соответствующих разделах.

```csharp
// Команды
public interface ICommand<TResponse>                      // без входных данных
{ Task<TResponse> ExecuteAsync(CommandContext context); }

public interface ICommand<TRequest, TResponse>
{ Task<TResponse> ExecuteAsync(TRequest request, CommandContext context); }

public readonly record struct Empty { public static readonly Empty Value = default; }   // «нет результата»
// Решение 17: без входа — ICommand<TResponse>; без результата — ICommand<TRequest, Empty>;
// ни того, ни другого — ICommand<Empty>. Empty уходит во фронтенд как JSON null.
// Класс, реализующий оба интерфейса сразу, — ошибка при Build() (раздел 5.3).

public sealed record CommandRequest(string Command, JsonElement Payload, string Source)
{
    public IReadOnlyDictionary<string, string> Headers { get; init; }   // как HTTP-заголовки; модуль их не читает (раздел 7)
    public IProgress<ProgressInfo>? Progress { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

public sealed class CommandContext
{
    public CommandRequest Request { get; }                // вызов как есть: payload, заголовки
    public CommandDescriptor? Descriptor { get; }         // имя, класс, типы, описание
    public string CommandName { get; }
    public string Source { get; }                         // "webview2", "ribbon", позже "mcp"
    public IProgress<ProgressInfo> Progress { get; }
    public CancellationToken CancellationToken { get; }
    public JsonElement RawPayload { get; }                 // для middleware; команде приходит уже TRequest
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandAttribute(string? name = null) : Attribute
{
    public string? Description { get; set; }               // больше ничего: правила документа — в приложении (решение 29)
}

// Вызов команды из C# — например, из кнопки ленты (раздел 8)
public interface ICommandSender
{
    void Send<TCommand>(object? payload = null);           // в конвейер, без ожидания; Source = "ribbon"
}

// Доступ к Revit — поверх AsyncExternalEvent из Nice3point.Revit.Toolkit; про документы модуль не знает
public interface IRevitDispatcher
{
    Task<T> InvokeAsync<T>(Func<UIApplication, T> work, CancellationToken ct = default);
    Task InvokeAsync(Action<UIApplication> work, CancellationToken ct = default);
}

// В приложении, не в модуле (Services/Documents/, раздел 7) — правило документа шаблона, можно заменить своим
public sealed class ActiveDocument(IRevitDispatcher dispatcher)     // scoped: один на вызов команды
{
    public string? Key { get; }                                       // ключ документа вызова, когда известен
    public static string GetKey(Document document);                   // путь файла, у несохранённого — заголовок
    public Task<T> InvokeAsync<T>(Func<Document, T> work, CancellationToken ct = default);
    public Task InvokeAsync(Action<Document> work, CancellationToken ct = default);
    public Task<T> InvokeUIAsync<T>(Func<UIDocument, T> work, CancellationToken ct = default);   // выделение, виды
    // нет документа → CommandException("no_document"); активный не тот → CommandException("document_changed")
}

// Middleware
public delegate Task<object?> CommandDelegate(CommandContext context);
public interface ICommandMiddleware { Task<object?> InvokeAsync(CommandContext context, CommandDelegate next); }

// Окна WebView2 (модуль Hosting.WebView, раздел 9)
public interface IWindows
{
    void Open(string route, Action<WebViewWindowOptions>? configure = null);   // одно окно на маршрут
    void ShowDockPane(string route);
}

// Уведомление пользователя. Реализация в Hosting — TaskDialog через IRevitDispatcher; Hosting.WebView может заменить
// её toast во фронтенде. Вызывается из любого потока, не ждёт закрытия диалога.
public interface IUserNotifier { void Error(string title, string message); void Info(string title, string message); }

// Кнопки ленты (раздел 8) — обёртки над Nice3point.Revit.Extensions; в приложении, Infrastructure/Ribbon/ (решение 35)
public static class RibbonButtons
{
    // Как panel.AddPushButton<T>(), но кнопка указывает на прокси с тем же именем в Launcher (раздел 8).
    public static PushButton AddButton<TCommand>(this RibbonPanel panel, string text) where TCommand : IExternalCommand, new();
    public static PushButton SetAvailability<TAvailability>(this PushButton button) where TAvailability : IExternalCommandAvailability, new();
}

// Жизненный цикл Revit поверх стандартного IHostApplicationLifetime (раздел 6)
public interface IRevitLifetime
{
    Task Initialized { get; }                  // завершается на ApplicationInitialized: UIApplication есть, можно в модель
    bool IsInitialized { get; }
}
```

### 4.1. Host — по образцу Nice3point (решения 16, 22)

- `Host.cs` в приложении — статический класс с `HostApplicationBuilder` (`DisableDefaults = true`, `ContentRootPath` —
  папка плагина), как в `MultiProjectSolution`. Своей обёртки-builder (`RevitAppBuilder`) нет.
- **Что даёт Generic Host:** `IHostedService` / `BackgroundService` (старт, остановка, таймауты, ошибки),
  `IOptions<T>.ValidateOnStart()`, совместимость с библиотеками под `IHostApplicationBuilder` (позже MCP SDK).
- **Окружение:** `DisableDefaults` не задаёт окружение — ставим сами: `Development` в Debug, `Production` в Release
  (через `HostApplicationBuilderSettings.EnvironmentName`), чтобы работали `IsDevelopment()` и
  `appsettings.Development.json`.
- **Сервисы в кнопках** — `Host.GetService<T>()`, как у Nice3point: Revit сам создаёт классы `ExternalCommand`,
  внедрить зависимости через конструктор там нельзя. Только в кнопках; логика — в командах и сервисах с обычным DI.
- **Проверка DI при `Build()` включается явно.** С `DisableDefaults = true` `HostApplicationBuilder` не включает
  `ValidateOnBuild` / `ValidateScopes`, поэтому ServiceDefaults задаёт их сам:
  `builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))`.
  Без этого незарегистрированный сервис всплывёт только при первом клике.
- **Зависания при старте нет.** `AsyncExternalApplication` ждёт `OnStartupAsync`, прокачивая очередь сообщений
  (`Dispatcher.PushFrame`), а продолжение снимает кадр через `TaskScheduler.Default` — обычный `await` в
  hosted-сервисах Revit не повесит. Код после `await` в `OnStartupAsync` снова выполняется в потоке Revit (проверено по
  исходникам Toolkit).

## 5. Команды (модуль `Davydov.Revit.Hosting`)

### 5.1. Сериализация — System.Text.Json

- Везде STJ: протокол WebView2, model binding команд, `appsettings.json`, ответы. Newtonsoft не используется
  (в AnalyseTool он был в публичном контракте — `JToken`, `RevitPayload`).
- Одни общие `JsonSerializerOptions` в DI (`IOptions<JsonSerializerOptions>`, настраиваются в ServiceDefaults —
  `ConfigureJsonSerializer`).
  По умолчанию: camelCase, регистронезависимое чтение, enum как строки. ServiceDefaults Nice3point добавляет в
  `TypeInfoResolverChain` свой `JsonSerializerContext` (source-generated) — после этого в цепочке **только** он, и типы,
  которых в нём нет, не сериализуются. Поэтому в цепочку добавляется ещё `new DefaultJsonTypeInfoResolver()`:
  частые типы — быстро через контекст, остальные `TRequest`/`TResponse` — через reflection, без ручной регистрации.
- Payload приходит как `JsonElement` и десериализуется в `TRequest` один раз, в конце конвейера (model binding).
- В модуле — конвертеры для типов Revit, которые встречаются в DTO: `ElementId` ↔ `long` и т.п.
- **Версии (решение 22):** как у Nice3point — одна версия Microsoft.Extensions.\* (10.x) для всех версий Revit,
  включая net8; на R25/R26 System.Text.Json приходит NuGet-пакетом. От конфликтов с другими плагинами защищает
  изоляция — свой ALC через Launcher на всех версиях Revit (решение 26). Версии собраны в одном месте —
  `Directory.Packages.props`; плавающие версии пакетов Nice3point (`$(RevitVersion).*`) требуют
  `CentralPackageFloatingVersionsEnabled=true`.
- Следствие: `JsonSchemaExporter` (MCP-схемы, фаза 7) доступен на всех версиях Revit.
- Newtonsoft допустим только если его требует сторонняя библиотека, и только внутри её обёртки.

### 5.2. Имена команд

- По умолчанию имя команды = имя класса (`RenameFamily` → `"RenameFamily"`), атрибут `[Command]` не обязателен.
- `[Command("Name")]` — только чтобы переопределить имя (например, сохранить старое имя после переименования класса).
- Суффикс `Command` отбрасывается, как `Controller` в ASP.NET Core: `RenameFamilyCommand` → `"RenameFamily"`.
- Сравнение без учёта регистра.
- Встроенные команды модулей имеют префикс `Host.` (`Host.GetCommands`, позже `Host.PickFolder`, `Host.OpenExternalLink`).
  Точка в имени допустима **только** у встроенных команд, поэтому с командами приложения они не пересекаются.
- Конфликт имён (в том числе между приложением и `Host.*`) — **ошибка при `Build()`** с указанием обоих классов,
  а не тихий пропуск с записью в лог, как в AnalyseTool.
- Защита от переименований — генерация TS-типов из команд (фаза 6): переименовал класс → фронтенд не скомпилируется.

### 5.3. Регистрация — reflection, как в AnalyseTool (решение 15)

- `builder.AddRevitCommands(c => c.FromAssemblyOf<Application>())` сканирует указанные сборки (приложение и, если есть,
  проекты-модули фич), как `CommandDispatcher.RegisterBuiltIns` в AnalyseTool: не абстрактные классы с
  `ICommand<TResponse>` / `ICommand<TRequest, TResponse>` → DI (scoped) + реестр с именем и описанием.
- Встроенные `Host.*`-команды наших модулей регистрируются автоматически.
- `SafeGetTypes`: при `ReflectionTypeLoadException` берём загрузившиеся типы, остальное — в лог с именем
  недостающей зависимости (урок AnalyseTool).
- Правила имён (раздел 5.2) живут в одном месте — `CommandNaming` в модуле.
- **Проверки при `builder.Build()`** — до первого клика, с понятным текстом; при ошибке — сбой старта: Launcher пишет `startup-error.txt` и отдаёт исключение Revit, решение 36 (раздел 8):

  | Проверка | Пример сообщения |
  | --- | --- |
  | одинаковые имена команд | «"GetFamilies" объявлена дважды: Davydov.Revit.Addin.Features.Families.GetFamilies и Davydov.Revit.Addin.Features.Library.GetFamiliesCommand» |
  | `TRequest` — интерфейс или абстрактный класс | «RenameFamilyRequest: System.Text.Json не сможет создать абстрактный тип» |
  | класс реализует несколько `ICommand<…>` | «PurgeFamilies реализует ICommand дважды» |
  | точка в имени команды приложения | «точка зарезервирована за Host.*» |
  | незарегистрированные зависимости | `ValidateOnBuild` (включается в ServiceDefaults, раздел 4.1): «IFamiliesService не зарегистрирован (нужен GetFamilies)» |

- Вызов: при регистрации для каждой команды один раз собирается типизированный делегат вызова, поэтому reflection не
  выполняется на каждый вызов.
- Команды только для отладки — обычный `#if DEBUG` вокруг класса.
- `ActiveUIDocument` в `Features/` и `Services/` запрещён (кроме самого `ActiveDocument`) — проверяет
  скрипт в CI (раздел 7).

### 5.4. Конвейер middleware

`ExceptionHandling` встроен (решение 18): модуль сам ставит его первым — забыть или поставить не туда нельзя. Свои
слои — в `AddRevitCommands(c => c.Use…)`, в порядке вызова.

```
[встроено] ExceptionHandling → [c.Use…: CommandLogging, SingleFlight, DocumentCheck, свои] → команда
```

| # | Middleware | Что делает | Подключение |
| --- | --- | --- | --- |
| 1 | `ExceptionHandling` | любое исключение → ответ `{ error }`; если у вызова нет окна-получателя (`Source = "ribbon"`) — `IUserNotifier.Error` | встроен, всегда первым |
| 2 | `CommandLogging` | кто, что, откуда, сколько длилось, чем кончилось | `c.UseCommandLogging()` |
| 3 | `SingleFlight` | не даёт запустить ту же команду второй раз, пока первая не закончилась (двойной клик) | `c.UseSingleFlight()`, по желанию |
| 4 | `DocumentCheck` | **в приложении:** ключ из заголовка `Document` фиксирует документ вызова в `ActiveDocument` (раздел 7) | `c.Use<DocumentCheckMiddleware>()` |
| — | свои слои | `c.Use<T>()` | между встроенным и командой |
| — | конечная точка | model binding (`JsonElement` → `TRequest`) + вызов команды | всегда последней |

Встроенного подтверждения опасных команд нет (решение 19). Если действие нужно подтвердить, это делает тот, кто его
вызывает: страница React — своим диалогом, кнопка — `TaskDialog` перед `Send<T>()`. Большинство изменений модели и так
отменяется через Ctrl+Z.

Middleware не содержит бизнес-логики и не ходит в модель: выполняется вне потока Revit, видит только запрос
(`context.Request`: payload и заголовки). Middleware из `c.Use<T>()` создаётся в scope вызова и может брать scoped-сервисы.

### 5.5. Потоки, прогресс и долгие команды

- **Транспорт сразу уводит вызов из UI-потока.** `WebMessageReceived` приходит в UI-потоке Revit (он же поток
  Revit API). Транспорт запускает конвейер в thread pool (`Task.Run`), а ответы и прогресс отправляет обратно через
  `Dispatcher` окна. Код команд и middleware **никогда** не выполняется в UI-потоке.
- **`InvokeAsync` выполняется синхронно в потоке Revit**, а это и UI-поток. Пока тело работает, окно не получает
  ни прогресс, ни что-либо ещё. Для одного долгого `InvokeAsync` прогресс бесполезен.
- **Долгая работа — порциями:** `document.InvokeBatchedAsync(items, batchSize: 200, (doc, batch) => …,
  context.Progress, context.CancellationToken)` — метод `ActiveDocument`, появится вместе с примером прогресса (фаза 4). Между порциями Revit получает управление: окно показывает прогресс,
  пользователь может отменить.
- **Цена порций при записи:** транзакцию и `TransactionGroup` нужно закрыть до выхода из обработчика external event,
  поэтому запись порциями даёт **несколько шагов отмены**. Команда выбирает: одна транзакция (один шаг отмены, без
  прогресса) или порции (прогресс и отмена, несколько шагов). Для чтения порции бесплатны.
- `IRevitDispatcher` строится на `AsyncExternalEvent` из Toolkit (можно вызывать с любого потока, вызовы идут по
  очереди, есть отмена). Совет Toolkit — не вызывать `.Wait()`/`.Result` в главном потоке Revit — входит в правила
  шаблона.

## 6. Жизненный цикл Revit (модуль `Davydov.Revit.Hosting`)

Событий Revit в шаблоне пока нет — ни механизма в модулях, ни примера подписки, ни push во фронтенд (решение 31).
Плагин, которому они нужны, подписывается сам (`+=`). От модуля — только жизненный цикл.

Стандартные события Generic Host сохраняют свой смысл, момент «Revit готов» — отдельное событие. Связать
`ApplicationStarted` с `ApplicationInitialized` нельзя: `ApplicationInitialized` Revit присылает **после** выхода из
`OnStartup`, а ждать его внутри `OnStartup` — гарантированное зависание.

| Момент | Событие | Что доступно |
| --- | --- | --- |
| `OnStartupAsync`, хост запущен | `IHostApplicationLifetime.ApplicationStarted` | DI, конфигурация; лента строится сразу после; `UIApplication` и документов ещё нет |
| Revit `ApplicationInitialized` | `IRevitLifetime.Initialized` | `UIApplication` есть, можно идти в модель через `IRevitDispatcher` |
| `OnShutdownAsync` | `ApplicationStopping` → `StopAsync` → `ApplicationStopped` | остановка hosted-сервисов |

```csharp
public sealed class WarmUpCache(IRevitLifetime revit, IFamiliesService families) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await revit.Initialized.WaitAsync(stoppingToken);   // ждём готовности Revit, не блокируя OnStartup
        await families.WarmUpAsync(stoppingToken);
    }
}
```

## 7. Документы: пользователь может сменить активный документ в любой момент

### Что может пойти не так

1. **Изменение уходит не в тот документ.** Окно показывает данные документа A, пользователь переключился на B и нажал
   «Переименовать». Команда берёт `ActiveUIDocument` (= B) и делает `GetElement(id)` с `ElementId` из A.
   `ElementId` уникален только внутри документа — в B может тихо измениться другой элемент.
2. **Долгая команда разъезжается между документами** — между двумя `InvokeAsync` пользователь переключился.
3. **Документ закрыли** во время команды или пока окно было открыто.
4. **Открылся редактор семейств** — активным стал документ семейства, команда рассчитана на проект.

### Решение: правило документа — в приложении, не в модуле (решение 29)

Модуль `Davydov.Revit.Hosting` про документы не знает: у него есть `IRevitDispatcher` (`UIApplication`) и заголовки
запроса, которые он не читает. С каким документом работать и что делать при смене, решает плагин. В шаблоне это
scoped-сервис приложения `ActiveDocument` и `DocumentCheckMiddleware` (`Services/Documents/`); плагину с другими
правилами достаточно заменить их своими.

1. **Команда работает с активным документом, зафиксированным на всю команду:** `document.InvokeAsync(doc => …)`.
   Первый доступ запоминает ключ документа; если к следующему доступу пользователь переключился — ошибка
   `document_changed` («Активный документ сменился — обновите данные»), в другой документ команда не пишет. Открытых
   документов нет — `no_document`. Команда, которой документ не нужен (`Host.GetCommands`, настройки), просто не
   берёт `ActiveDocument`.
2. **Ключ документа** — `ActiveDocument.GetKey`: путь файла, у несохранённого — заголовок. Два экземпляра одного
   файла Revit открыть не даёт. После «Сохранить как» ключ меняется — вызывающий перезагружает данные, как после смены
   документа. Реестра открытых документов нет.
3. **Данные одного документа не применяются к другому.** Вызывающий передаёт ключ документа, из которого взяты данные,
   в заголовке `Document` (`CommandRequest.Headers`; от фронтенда —
   `{"type":"request","id":"7","command":"RenameFamily","headers":{"Document":"C:\\…\\A.rvt"},"payload":{…}}`).
   `DocumentCheckMiddleware` фиксирует его в `ActiveDocument` до первого доступа: активен B — отказ. Кнопка ленты
   заголовок не передаёт — документ берётся на первом доступе команды.
4. **Выполнения в неактивном документе нет.** Только активный; действия с интерфейсом — через `InvokeUIAsync`:

   ```csharp
   public sealed class SelectElements(ActiveDocument document) : ICommand<SelectElementsRequest, Empty>
   {
       public Task<Empty> ExecuteAsync(SelectElementsRequest request, CommandContext context) =>
           document.InvokeUIAsync(ui =>
           {
               ui.Selection.SetElementIds(request.Ids);
               return Empty.Value;
           }, context.CancellationToken);
   }
   ```

5. **Тип документа** (проект или семейство) проверяет сама команда
   (`if (doc.IsFamilyDocument) throw new CommandException("project_only", "…")`), кнопку гасит `ProjectAvailability`.
   Атрибутов для этого нет.
6. **Документ закрыли во время команды** — следующий доступ даст `no_document` или `document_changed`; токена
   закрытия нет.
7. **Защита в CI:** `build/Check-Boundaries.ps1` запрещает `ActiveUIDocument` в `Features/` и `Services/`,
   в том числе внутри `IRevitDispatcher.InvokeAsync(Func<UIApplication, T>)`. Исключения — сам
   `Services/Documents/ActiveDocument.cs` и строка с комментарием `// AllowActiveDocument: причина`.
   В кнопках (`Commands/`, раздел 8) активный документ использовать можно: клик и относится к нему.

### Фронтенд: окно следует за активным документом

- Закреплённых окон нет: окна и док-панель работают с активным документом. `useActiveDocument()` даёт его ключ и
  заголовок — из команды приложения `GetActiveDocument` (при загрузке страницы и после `document_changed`).
- `useCommand("GetFamilies")` сам добавляет заголовок `Document`, ключ кэша — `[documentKey, "GetFamilies"]`.
  Вернулся в A — данные A мгновенно из кэша.
- Push-событий нет (решение 31): о смене документа страница узнаёт из ответа `document_changed` на своё следующее
  действие — перезапрашивает `GetActiveDocument` и данные нового документа.

### Сценарии

| Ситуация | Поведение |
| --- | --- |
| Переключился A → B и сразу нажал кнопку в окне с данными A | отказ «Документ сменился — обновите данные», окно перезапрашивает данные B |
| Переключился посреди долгой команды | следующий доступ к документу — отказ; уже сделанное в A остаётся (транзакции закрыты) |
| Закрыл документ во время команды | следующий доступ — отказ |
| Док-панель, переключение A → B | данные A видны до следующего действия; оно получает `document_changed`, и панель перезапрашивает данные B (возврат в A — из кэша) |
| Открыл редактор семейств | это смена документа; команда «только проект» отвечает своей понятной ошибкой |
| Вызов с ленты | активный на первом доступе, зафиксирован на всю команду |

## 8. Лента и вход из Revit — как у Nice3point (решение 22)

### Вход и изоляция (решение 26)

Своей изоляции в текущем Toolkit нет: в `CHANGELOG.md` Toolkit сказано, что `AssemblyLoadContext` удалён и изоляция
переезжает в сам Revit. Базовые классы `ExternalApplication` / `ExternalCommand` только ищут зависимости в папке
плагина (`ResolveHelper`), если сборка загружена в обычный контекст. Штатная изоляция Revit 2026+ (`ManifestSettings`)
по сообщениям на форуме Autodesk в 2026.1 работает ненадёжно, а в Revit 2025 её нет вовсе. Поэтому изоляция — **своя,
одинаковая на всех версиях**, как в AnalyseTool:

```
Revit 2025–2027 → Davydov.Revit.Addin.Launcher.addin (в проекте Launcher) → Davydov.Revit.Addin.Launcher.App
  ├─ свой AssemblyLoadContext + AssemblyDependencyResolver, как IsolatedAssemblyLoadContent в AnalyseTool
  ├─ в нём — Davydov.Revit.Addin.dll и все зависимости (Microsoft.Extensions.*, WebView2, Toolkit); Revit API — общий
  ├─ reflection — ОДИН раз: создать Davydov.Revit.Addin.Application внутри ALC и вызвать его OnStartup / OnShutdown
  │   (Toolkit в чужом ALC сам отключает ResolveHelper — проверено по исходникам)
  ├─ Davydov.Revit.Addin.Application.OnStartupAsync: Host.StartAsync() → CreateRibbon()
  └─ ошибка на любом шаге → Launcher пишет startup-error.txt и пробрасывает исключение — Revit сам показывает окно ошибки
```

- Манифест один — в Launcher, без `ManifestSettings`: Launcher без зависимостей грузится в контекст Revit, всё
  остальное живёт в нашем ALC. `DeployAddin` включён в Launcher (SDK требует его в проекте с манифестом); Launcher
  ссылается на приложение ради порядка сборки и `deps.json`, поэтому в папку плагина попадает всё.
- Имя класса приложения (`Davydov.Revit.Addin.Application`) Launcher берёт из константы (`PluginInfo`), которую шаблон
  подставляет при создании проекта. Если класс не найден — понятное исключение: Revit покажет его текст, полный текст — в `startup-error.txt`.
- **Сделано в фазе 1:**
  - зависимости для ALC берутся из `deps.json` **Launcher** (у проектов, на которые ссылаются, своего `deps.json` в
    папке плагина нет), как в AnalyseTool;
  - сбой старта: `Application.OnStartupAsync` не ловит исключения, они доходят до Launcher, который пишет
    `%LOCALAPPDATA%\<плагин>\logs\<год>\startup-error.txt` и пробрасывает исключение плагина (не обёртку
    reflection-вызова) — Revit сам показывает окно «не удалось запустить внешнее приложение» с его текстом (решение 36);
  - Launcher сообщает модулю свой путь и пространство имён прокси через `AppContext.SetData` (данные процесса, видны
    из обоих контекстов без общих типов) — их читает `LauncherInfo` в приложении (`Infrastructure/Ribbon/`, рядом с Launcher, решение 35); ключи — с именем плагина;
  - иконки — встроенные ресурсы (`EmbeddedResource`) и `Icons.WithIcons(...)`: pack URI ищет сборку по имени и мог
    бы загрузить вторую копию приложения вне ALC;
  - `DeployAddin` — только в Debug-конфигурациях; Release публикует в `bin\Release.R2x\publish`;
  - манифест называется `Davydov.Revit.Addin.Launcher.addin` — так его находит и удаляет при `Clean` SDK Nice3point.
- Это отклонение от структуры Nice3point (у них манифест в приложении с `ManifestSettings`). Если штатная изоляция
  Revit когда-нибудь станет надёжной, Launcher можно убрать: приложение и кнопки уже написаны в стиле Nice3point.

### Кнопки — классы `ExternalCommand` в `Commands/`

```csharp
// source/Davydov.Revit.Addin/Commands/PurgeFamiliesCommand.cs
[Transaction(TransactionMode.Manual)]
public class PurgeFamiliesCommand : ExternalCommand
{
    public override void Execute() => Host.GetService<ICommandSender>().Send<PurgeFamilies>();
}

// source/Davydov.Revit.Addin/Commands/FamiliesCommand.cs
[Transaction(TransactionMode.Manual)]
public class FamiliesCommand : ExternalCommand
{
    public override void Execute() =>
        Host.GetService<IWindows>().Open("/families", o => { o.Width = 1100; o.Height = 720; });
}
```

```csharp
// Application.CreateRibbon — Nice3point.Revit.Extensions + наши обёртки AddButton / SetAvailability
var panel = Application.CreatePanel("Семейства", "Davydov.Revit.Addin");
panel.AddButton<FamiliesCommand>("Семейства")
    .WithIcons("Families");                                // Families16.png / Families32.png — встроенные ресурсы
panel.AddButton<PurgeFamiliesCommand>("Очистить")
    .SetAvailability<ProjectAvailability>();               // IExternalCommandAvailability

#if DEBUG
var dev = Application.CreatePanel("Dev", "Davydov.Revit.Addin");        // Debug-панель для проверки команд (раздел 12)
dev.AddButton<PingCommand>("Ping");
#endif
```

```csharp
// source/Davydov.Revit.Addin.Launcher/Proxies.cs — по строке на кнопку и на класс доступности
public sealed class FamiliesCommand : ProxyCommand;
public sealed class PurgeFamiliesCommand : ProxyCommand;
public sealed class PingCommand : ProxyCommand;
public sealed class ProjectAvailability : ProxyAvailability;

// ProxyCommand (пишется один раз): находит в ALC класс с ТЕМ ЖЕ именем, создаёт и вызывает Execute
```

Почему прокси: Revit создаёт класс кнопки сам по пути к DLL и имени типа. `AddPushButton<T>` из Extensions берёт путь
сборки `T` — Revit загрузил бы `Davydov.Revit.Addin.dll` второй раз, в обычный контекст, в обход Launcher (и со своей пустой копией
`Host`). Обёртка `AddButton<T>` подставляет путь Launcher и имя прокси, а при старте проверяет, что прокси есть: если
нет — ошибка «Добавь в Davydov.Revit.Addin.Launcher/Proxies.cs: `public sealed class PurgeFamiliesCommand : ProxyCommand;`».

Правила:
- **Кнопка тонкая.** Она открывает окно (`IWindows`), показывает док-панель или отправляет команду конвейера
  (`ICommandSender.Send<T>()`). Логика — в командах (`Features/`) и сервисах.
- **`Send<T>()` не ждёт выполнения.** Вызов уходит в конвейер (`Source = "ribbon"`, документ — активный на первом
  доступе команды, раздел 7) **без ожидания**: команда сама пойдёт через external event и ждала бы событие, внутри которого стоит клик
  (урок `InvokeSavedCommand` из AnalyseTool). Ошибки — через `IUserNotifier`.
- **Окна.** `IWindows.Open(route)` открывает общий `WebViewWindow` (раздел 9); второй клик поднимает уже открытое окно
  этого маршрута, как `_familyWindow` / `Restore` в `RibbonHost`.
- **Стабильный id кнопки.** По внутреннему имени кнопки Revit привязывает горячие клавиши, панель быстрого доступа и
  `PostCommand`. `AddPushButton<T>` берёт имя `T.FullName` — **вместе с пространством имён**: переименование класса и
  перенос в другую папку или namespace сбрасывают привязки пользователя. Один класс кнопки — одна кнопка в панели.
- **Доступность** — обычные `IExternalCommandAvailability` (`ProjectAvailability`, `DocumentAvailability` — в приложении, `Infrastructure/Ribbon/`),
  ставятся через `SetAvailability<T>()` (класса `ExternalCommandAvailability` в текущем Toolkit нет), тоже через прокси
  в Launcher. Условие обязано быть быстрым, исключение в нём считается «недоступно».
- **Док-панель** регистрируется при старте через `DockablePaneProvider.Register(...)` из Toolkit (Revit разрешает это
  только в `OnStartup`); содержимое — `WebViewWindow`-контент модуля, маршрут меняется без перезапуска.
- **Изменения во время работы** — через официальный API уже созданных кнопок (`Visible`, `Enabled`, `ItemText`,
  `ToolTip`, `Image`). Новые кнопки после старта не добавляются.

### Почему не AdWindows

AdWindows (как у кнопок расширений AnalyseTool) не требует классов, но для Revit такая кнопка — просто элемент
интерфейса: нет горячих клавиш, `PostCommand` и журналов, доступность — вручную, API недокументирован. Расширений нет
(решение 12), а у Nice3point кнопки — обычные `ExternalCommand`.

## 9. WebView2: окружение и безопасность (модуль `Davydov.Revit.Hosting.WebView`)

- **Одно окружение на плагин** (`WebViewEnvironmentProvider`): проверка наличия runtime
  (`GetAvailableBrowserVersionString`; нет — понятное сообщение со ссылкой на установку), `UserDataFolder` в
  `%LOCALAPPDATA%\<Tool>\WebView2` (папка Revit недоступна для записи). В AnalyseTool это повторено в пяти окнах.
- **Одно окно на все случаи — `WebViewWindow`.** В AnalyseTool `FamilyControlWindow`, `SettingsWindow`, … — копии
  одного класса (~30 строк), отличаются заголовком, размером и маршрутом. В шаблоне это один класс с опциями
  (`Title`, `Width`, `Height`): `Owner` = главное окно Revit, общее окружение, транспорт `Attach`/`Detach`,
  переход на маршрут, одно окно на маршрут. Особое поведение — опциями, в крайнем случае наследованием.
  Тот же контент — в док-панели (раздел 8).
- **Контент:** Release — `SetVirtualHostNameToFolderMapping("app.<tool>.local", dist)`; Debug — dev-сервер Vite
  (`ClientApp:DevServerUrl` в настройках, по умолчанию `http://localhost:5173`).
- **Защита навигации — включена по умолчанию (решение 34).** Любая страница, загруженная в WebView, может вызывать
  команды через `postMessage`, поэтому чужой сайт в окне получил бы доступ к модели. Выключается одной строкой
  `AddWebView(application, w => w.AllowAnyNavigation())` — только если плагин должен показывать в своём окне другой,
  доверенный сайт:
  - `NavigationStarting` — переход на любой адрес, кроме своего virtual host (или dev-сервера в Debug), отменяется,
    `http(s)`-адрес открывается в системном браузере;
  - `NewWindowRequested` — `Handled = true`, `http(s)`-ссылки открываются в системном браузере;
  - `WebMessageReceived` — сообщения не с нашего origin отбрасываются и пишутся в лог.
- **Настройки WebView** (всегда): в Release `AreDevToolsEnabled = false` и стандартное контекстное меню выключено,
  `AreHostObjectsAllowed = false`, зум выключен.
- **Устойчивость:** отправка в окно, закрытое посреди команды, не бросает исключений (как в `WebView2Transport`).

## 10. Фронтенд

**Фронтенд в шаблоне минимальный (решение 20).** Шаблон отвечает только за связь фронтенда с Revit — то, что
завязано на протокол и возможности наших модулей. Внешний вид, компоненты и библиотеки интерфейса выбирает каждый
проект.

**Входит в шаблон:**

1. **`revit-bridge`** — чистый TypeScript, без зависимостей от React; живёт в модуле `Davydov.Revit.Hosting.WebView`
   и подключается в `ClientApp` локальным npm-пакетом. Переносится из `RevitBridge.ts` AnalyseTool:
   `invoke(command, payload, { onProgress, signal })`,
   сопоставление Request/Response по `id`, заголовок `Document` (раздел 7). В Debug без Revit (в обычном браузере) —
   mock-режим. Тесты — Vitest.
2. **React-хуки поверх моста** — `useCommand` / `useCommandMutation` (на TanStack Query: кэш по документу, состояние
   загрузки, прогресс), `useActiveDocument` (раздел 7).
3. **Пустое приложение Vite + React + TypeScript** (`source/Davydov.Revit.Addin/ClientApp`) с одной страницей-примером: вызов команды
   с прогрессом и реакцией на `document_changed`. Сборка в `dist`, который отдаёт `WebViewWindow` (раздел 9), dev-сервер с
   HMR в Debug. В поставку `dist` попадает как `<Content Include="ClientApp/dist/**" CopyToPublishDirectory="PreserveNewest"/>`
   (так SDK Nice3point добавляет файлы в publish и deploy); `npm run build` — шаг перед publish в ModularPipelines.

**Не входит в шаблон:** библиотека компонентов, таблицы, деревья, роутинг, состояние, графики — проект подключает
сам, что ему нужно.

Пример вызова команды:

```tsx
const families = useCommand<FamilyDto[]>("GetFamilies");   // при document_changed — сам перезапрашивает данные
const rename = useCommandMutation("RenameFamily");

if (families.isLoading) return <Spinner />;
return <FamiliesTable data={families.data} onRename={r => rename.mutate(r)} />;   // компонент проекта, не шаблона
```

Позже (фаза 6): типы `TRequest`/`TResponse` для TypeScript генерировать из C#, чтобы фронтенд и бэкенд не расходились.

## 11. Настройки и логи (`Davydov.Revit.Addin.ServiceDefaults` + модуль)

- **ServiceDefaults — как у Nice3point:** `AddLoggingDefaults` (секция `Logging`, провайдер Debug, журнал Revit через
  `Nice3point.Revit.Logging` с уровнем `Error`), `AppDomainExceptionsHandler` (необработанные исключения — в лог),
  `JsonSerializerContext`. Это проект приложения, его можно менять.
- **Наши правки в ServiceDefaults:** `ValidateOnBuild` / `ValidateScopes` (раздел 4.1), `DefaultJsonTypeInfoResolver`
  в цепочке JSON (раздел 5.1), слои конфигурации и файловый лог (ниже).
- **Наше дополнение в ServiceDefaults — слои конфигурации** (позднее перекрывает раннее):
  1. `appsettings.json` рядом с DLL — значения по умолчанию, только чтение;
  2. `appsettings.{Environment}.json` — `Development` в Debug (например, адрес dev-сервера и подробные логи);
  3. `%AppData%\<Tool>\<RevitVersion>\usersettings.json` — то, что пользователь поменял в интерфейсе;
  4. переменные окружения с префиксом из имени плагина (`DAVYDOV_REVIT_ADDIN_…`) — для отладки.
- **Чтение** — обычные `IOptions<T>` / `IOptionsMonitor<T>` (`reloadOnChange` для пользовательского файла).
- **Запись** — `IUserSettings` (модуль `Davydov.Revit.Hosting`): `await settings.UpdateAsync<AiOptions>("Ai", o => o.Model = "…")`
  пишет секцию в `usersettings.json`, `IOptionsMonitor` подхватывает изменение. Так переносятся `mcp.json` и
  настройки AI-провайдеров из AnalyseTool. Секреты (API-ключи) — не в JSON, а через DPAPI (`ProtectedData`).
- **Журнал Revit:** в Release — только `Warning` и выше (журнал хранит всю сессию и уходит в Autodesk с отчётом об
  ошибке); секреты и лишние личные данные туда не пишутся.
- **Файловый лог (решение 23)** — наше дополнение в ServiceDefaults: Serilog **как провайдер**
  (`builder.Logging.AddSerilog(...)`, не `UseSerilog()`, иначе журнал Revit перестанет получать записи), файл
  `%LOCALAPPDATA%\<Tool>\logs\<RevitVersion>\log-.txt`, уровень `Information`, ротация по дням. Каждая команда
  логируется с `CommandName` и `Source` (через `CommandLogging`). Журнал Revit — контекст сбоя рядом с
  действиями пользователя, файл — подробности работы плагина для поддержки.

## 12. Тесты и цикл разработки

Один тестовый фреймворк — **TUnit** — и для тестов без Revit, и для тестов внутри Revit (решение 21), как в шаблонах
Nice3point (`global.json` → Microsoft.Testing.Platform).

- **Unit-тесты без Revit** (`tests/Davydov.Revit.Hosting.Tests`): конвейер, middleware, model binding, реестр команд
  (правила имён, проверки `Build()`). Подменить `IRevitDispatcher` нельзя — в его сигнатурах `UIApplication`, а типы
  Revit вне Revit не загрузятся; поэтому без Revit тестируется только то, что Revit не трогает, а всё остальное — тестами
  внутри Revit. В этом проекте нет пакета Polyfill: через `InternalsVisibleTo` видны полифилы модуля, вторая копия
  дала бы неоднозначные вызовы (то же в `Davydov.Revit.Addin.Tests`).

  ```
  dotnet run --project tests/Davydov.Revit.Hosting.Tests -c Debug.R26   # Revit не нужен; так же на R25 и R27
  ```
- **Тесты внутри Revit** (`tests/Davydov.Revit.Addin.Tests`, Nice3point «Revit Test») —
  [Nice3point.TUnit.Revit](https://github.com/Nice3point/RevitUnit): тесты выполняются в контексте Revit API, DI —
  через `DependencyInjectionDataSourceAttribute`, как в `MultiProjectSolution`.

  ```csharp
  public sealed class ActiveDocumentTests : RevitApiUiTest   // каждый тест — в потоке Revit, в контексте Revit API
  {
      [Test]
      public async Task InvokeAsync_UserSwitchedDocumentsBetweenCalls_ThrowsDocumentChanged()
      {
          Activate(_pathA);
          var activeDocument = new ActiveDocument(new RevitDispatcher());   // AllowDirectInvocation: работа идёт сразу
          await activeDocument.InvokeAsync(document => document.Title);

          Activate(_pathB);

          var exception = await Assert.That(async () => { await activeDocument.InvokeAsync(d => d.Title); })
              .Throws<CommandException>();
          await Assert.That(exception!.Code).IsEqualTo(DocumentErrorCodes.DocumentChanged);
      }
  }
  ```

  ```
  dotnet test -c "Release.R26"      # или dotnet run; конфигурация выбирает версию Revit
  ```

  - Что проверяют: `IRevitDispatcher`, `ActiveDocument`, команды, которые меняют модель,
    с реальными документами. `RevitApiUiTest` — для того, что требует `UIApplication`.
  - Нужен установленный и лицензированный Revit той версии, что в конфигурации. Поэтому в облачном CI эти тесты не
    запускаются — локально или на своём раннере с Revit (фаза 5).
  - Путь запуска плагина (`.addin` → Launcher → ALC → `Host` → лента) тесты не
    проверяют — для этого Debug-панель «Dev» и короткий чек-лист в `docs/manual-tests.md`.
- **Бенчмарки** — проект Nice3point «Revit Benchmark» (BenchmarkDotNet в потоке Revit), по желанию.
- **Фронтенд** — Vitest (раздел 10).
- **Цикл разработки C#.** `DeployAddin` + `LaunchRevit` из SDK: сборка раскладывает плагин и запускает нужную версию
  Revit под отладчиком. В фазе 1 проверить .NET Hot Reload / Edit and Continue в изолированном контексте; результат
  записать в CLAUDE.md шаблона.

## 13. Фазы

0. **Решения** — открытых вопросов нет (H — решение 23, I — решение 24).
1. **Скелет на Nice3point** — структура `MultiProjectSolution` (раздел 3) для Revit 2025–2027: `Nice3point.Revit.Sdk`,
   `.slnx` с `Debug.R25…Release.R27`, `global.json`, CPM (`CentralPackageFloatingVersionsEnabled`), `Application.cs` +
   `Host.cs` + `ServiceDefaults` (с `ValidateOnBuild` и JSON-резолвером), `Davydov.Revit.Addin.Launcher` с `.addin`, своим ALC и
   прокси, обёртки `AddButton<T>` / `SetAvailability<T>`, одна кнопка, ~~запасная панель~~ (решение 36), `DeployAddin`/`LaunchRevit`
   в Launcher. Из `.csproj` Nice3point переносится `EnableDynamicLoading=true`; `IsRepackable=false` (изоляция есть).
   Проверить: Launcher и ALC рядом с AnalyseTool на 2025, 2026 и 2027 (раздел 8), WebView2 в изолированном контексте,
   Hot Reload.
   Готово, когда: кнопка на ленте пишет в журнал Revit на 2025, 2026 и 2027; при исключении в `Host.StartAsync`
   ~~появляется запасная панель~~ (с решения 36 — `startup-error.txt` и окно ошибки Revit); плагин работает рядом с AnalyseTool; без прокси в Launcher кнопка даёт понятную
   ошибку при старте.
2. **Модуль `Davydov.Revit.Hosting`, основа** — `IRevitDispatcher` поверх `AsyncExternalEvent`, `IRevitLifetime`,
   слои конфигурации в ServiceDefaults и `IUserSettings`, `IUserNotifier`, окружение `Development`/`Production`.
   Готово, когда: сервис из DI читает документ через `IRevitDispatcher` после `IRevitLifetime.Initialized`;
   `BackgroundService` с обычным `await` не вешает запуск Revit; настройка, записанная через `IUserSettings`,
   переживает перезапуск Revit; измерено время старта хоста.
   **Сделано (код собран, проверка в Revit — `docs/manual-tests.md`, фаза 2):** `builder.AddRevitHosting(application)`
   регистрирует `IRevitDispatcher` (поверх `AsyncRequestExternalEvent` с `AllowDirectInvocation`), `IRevitLifetime` и
   `IUserNotifier` (TaskDialog через диспетчер); диспетчер и подписка на `ApplicationInitialized` создаются прямо в
   `AddRevitHosting` — внутри `OnStartup`, до первого `await`. ServiceDefaults добавляет слои конфигурации
   (`AddConfigurationDefaults`) и `IUserSettings` (`AddUserSettings`, запись через временный файл). `Host` пишет время
   старта в журнал. В Debug: `StartupProbe` (ждёт `Initialized`, читает модель через диспетчер) и кнопка «User settings».
   **Перенесено:** `ICommandSender` — в фазу 3 (нужен конвейер); `InvokeBatchedAsync` — в фазу 4, методом
   `ActiveDocument` вместе с примером прогресса. Перегрузок диспетчера с документом нет (решение 29). Секреты через DPAPI — когда понадобятся (MCP / AI-провайдеры).
3. **Команды** — `AddRevitCommands` (reflection-регистрация и проверки `Build()`, раздел 5.3), встроенная
   `Host.GetCommands`, конвейер (`ExceptionHandling` + `c.Use…`, раздел 5.4), запуск конвейера в thread pool,
   `ICommandSender`; в приложении — `ActiveDocument` и `DocumentCheckMiddleware` (раздел 7), Debug-кнопка
   «Active document» с командой `CheckActiveDocument`.
   Готово, когда: команда с DI и `TRequest` вызывается с ленты и проходит через middleware; смена документа посреди
   команды даёт `document_changed`; ошибка команды с ленты показывается через `IUserNotifier`; есть unit-тесты на
   конвейер и тесты внутри Revit (Nice3point.TUnit.Revit) на `IRevitDispatcher` и `ActiveDocument`.
   **Сделано (код собран, проверка в Revit — `docs/manual-tests.md`, фаза 3):** всё перечисленное; `AddRevitCommands`
   требует `IUserNotifier` из `AddRevitHosting`. Тесты: `Davydov.Revit.Hosting.Tests` — 28 тестов без Revit (имена,
   проверки регистрации, model binding, ошибки, порядок middleware и заголовки, `SingleFlight`, отмена,
   `ICommandSender`, `Host.GetCommands`), проходят на R25–R27; `Davydov.Revit.Addin.Tests` — диспетчер,
   `ActiveDocument` с двумя документами, ключ документа, `DocumentCheckMiddleware` — собраны, но не запускались
   (на машине сборки нет Revit).
4. **Модуль `Davydov.Revit.Hosting.WebView`** — окружение WebView2 и защита навигации по желанию (раздел 9), `WebViewWindow`,
   док-панель через `DockablePaneProvider`, транспорт, `revit-bridge` (TS), React-хуки, пустое React-приложение со
   страницей-примером, Vite HMR в Debug, `useActiveDocument` и команда приложения `GetActiveDocument`,
   `ActiveDocument.InvokeBatchedAsync`.
   Готово, когда: кнопка в React вызывает команду и показывает результат и прогресс (на `InvokeBatchedAsync`);
   после смены документа вызов с данными старого документа получает `document_changed`, и страница
   перезапрашивает данные нового; с защитой навигации переход по внешней ссылке открывает системный браузер.
   **Сделано (код собран, проверка в Revit — `docs/manual-tests.md`, фаза 4):** модуль `Davydov.Revit.Hosting.WebView`
   (`builder.AddWebView(application, w => w.DockPane(id, title, route))`): одно окружение WebView2 на плагин,
   `ClientAppSource` (dev-сервер в Development, `dist` через `https://app.<плагин>.local` в Release, hash-маршруты),
   `WebViewHost` (настройки, восстановление после сбоя процесса, защита навигации — с решения 34 включена по умолчанию, `AllowAnyNavigation()` выключает,
   сообщение без runtime), `WebViewWindow` и `IWindows` (одно окно на маршрут, док-панель через `DockablePaneProvider`
   из Toolkit), `WebViewTransport` (протокол request / cancel / progress / response; с защитой навигации — только свой origin). В приложении:
   `GetActiveDocument`, `CountElementsByCategory` на `ActiveDocument.InvokeBatchedAsync`, кнопки «Sample page» и
   «Palette». Фронтенд: `revit-bridge` (TS, без зависимостей, mock-режим, 8 тестов Vitest), `ClientApp` (Vite 8 +
   React 19 + TanStack Query, хуки в `src/revit/`, страница-пример; в браузере без Revit — на моках). Тесты
   `ClientAppSource` — в `Davydov.Revit.Hosting.Tests` (38 тестов).
   **После первого запуска в Revit 2025 (2026-10-25):** файловый лог Serilog (решение 23) — `AddFileLog` в
   ServiceDefaults, уровень из `Logging:Serilog`, общий файл для двух сессий одной версии; отмена команды клиентом
   пишется одной строкой `CommandLogging` (`canceled by the caller`), исполнитель — только `Debug`, не `Warning`
   (в Debug `StrictMode` React отменяет первый запрос при каждом открытии страницы). Тесты — 39.

5. **Сборка — как у Nice3point** — ModularPipelines (`build/`: compile, test, pack), WixSharp
   (`install/`) + bundle для Autodesk Store, GitHub workflow `Compile.yml`, версия из git-тегов (GitVersion).
   Выпуск (`publish`, `PublishRelease.yml`, CHANGELOG) перенесён в фазу 6 (решение 33). Плюс наше: проверка границ по таблице 3.1 и запрета `ActiveUIDocument` (раздел 7), сборка
   фронтенда в `pack`. Тесты внутри Revit — не в облачном CI: локально или на своём раннере.
   **Сделано — compile и test (2026-10-25):** `build/` по образцу шаблона Nice3point 6.2.3 — `ResolveConfigurations`,
   `ResolveVersioning` (GitVersion), `CompileClientApp` (`npm ci` / локально `npm install`, затем `npm run build`) и
   `CompileProject` для `Release.R25…R27`; версии пакетов — в общем `Directory.Packages.props` (Revit-пакеты — под
   условием `RevitVersion`, у `build/` его нет). С аргументом `test` — наши тесты: `Davydov.Revit.Hosting.Tests` на всех
   конфигурациях, Vitest в `revit-bridge`, `Davydov.Revit.Addin.Tests` — только не в CI (`SkipIf<IsCI>`).
   `.github/workflows/Compile.yml`: `push` в `main` и `pull_request`, `dotnet run -c Release -- test`.
   **Сделано — pack (2026-10-25):** `dotnet run -c Release -- pack` — `CleanProject` (локально; `node_modules` не трогает:
   у npm-пакетов свои `bin`), `CreateInstaller` (WiX 7 через `dotnet tool` с `wix eula accept wix7` — EULA OSMF принята,
   `install/` на WixSharp: `SingleUser` в `%AppData%`, `MultiUser` в `%ProgramData%`, для 2027 — в `%ProgramFiles%`;
   `AllowSameVersionUpgrades`, пока до первого тега все MSI — `0.0.1`) и `CreateBundle` (`<продукт>.bundle.zip` с
   `PackageContents.xml`) → `output/`. Имя продукта, UpgradeCode и производитель — в `build/appsettings.json`
   (`Product`, `Vendor`). Оба берут только `Release.R2x/publish` и падают без `ClientApp/dist/index.html`. Launcher
   убирает из сборки ассеты не-x64 платформ и XML-документацию WebView2 (`RemoveForeignPlatformAssets`). В CI `pack`
   не идёт — только в `PublishRelease.yml`.
   **Сделано — проверка границ (2026-10-25):** `build/Check-Boundaries.ps1` (PowerShell 7) — ссылки проектов `source/`
   по таблице 3.1 (новый проект без строки в скрипте — ошибка), headless `Davydov.Revit.Hosting` (`UseWPF`,
   `System.Windows`, `MessageBox`), без `System.Windows` и `ActiveUIDocument` в `Features/` и `Services/` приложения
   (кроме `ActiveDocument.cs` и строки с `// AllowActiveDocument: причина`), без типов приложения и модулей в коде
   Launcher; комментарии и строки не считаются. В пайплайне — `CheckBoundariesModule` при каждом запуске, в CI тоже.
   **Фаза 5 закрыта.**
6. **Шаблон** — свой `dotnet new davydov-revit -n Davydov.Revit.Addin` (решение 24): копия структуры Nice3point + наши модули в
   исходниках, параметры; в README шаблона — с какой версией примера Nice3point он сверен и как переносить их обновления.
   **Каждый новый проект получает свои идентификаторы** (`guids` и `sourceName` в `template.json`): `AddInId` в `.addin`,
   имя класса приложения для Launcher, имя ALC, GUID док-панели, UpgradeCode установщика (`build/appsettings.json`), имена папок WebView2,
   логов и `usersettings`, имя virtual host. Проверка: два плагина из шаблона одновременно в одном Revit. CLAUDE.md и навыки для ИИ-агентов
   (как `.agents/skills` у Nice3point: правила документов, команд, потоков, ленты); генерация TS-типов
   (утилита после сборки читает команды из DLL через `MetadataLoadContext` и пишет `ClientApp/src/generated/commands.ts`).
   **Выпуск (решение 33):** релиз этого репозитория — NuGet-пакет шаблона (`PackageType` `Template`, `dotnet pack`,
   публикация в nuget.org или GitHub Packages) своим workflow. Внутри шаблона, для плагинов из него, — как у
   Nice3point: `PublishRelease.yml` (тег или `workflow_dispatch` → `pack publish`), модули `GenerateChangelog`,
   `GenerateGitHubChangelog`, `PublishGithub` (GitHub Release с MSI и bundle, при ошибке тег удаляется), `CHANGELOG.md`,
   `permissions: contents: write`, `setup-dotnet` и `setup-node`. Проверяется в проекте, созданном из шаблона, в тестовом
   репозитории — не здесь: релиз плагина-заглушки смысла не имеет.
   **Тесты в шаблон не входят — они только для этого репозитория:** папка `tests/`, тесты `revit-bridge`, модули
   `build/Modules/Test*` и блок `test` в `build/Program.cs`; в `Compile.yml` шаблона — `dotnet run -c Release` без `test`.
   **Сделано — 6.1, шаблон и его пакет (2026-10-25):** репозиторий и есть шаблон (а не отдельные исходники с `#if`,
   как у Nice3point): `.template.config/template.json` — `davydov-revit`, `sourceName` `Davydov.Revit.Addin` (меняет и
   `Davydov_Revit_Addin` у Sourcy), производное `davydov-revit-addin` (`package.json`), новые `guids` (`AddInId`, GUID
   док-панели, UpgradeCode), параметр `--vendor` (`VendorId`, `Vendor:Name`). Что только для репозитория:
   исключения в `sources` (`tests/`, `docs/`, `template/`, `LICENSE`, `build/RepositoryModules.cs`, `Test*`-модули,
   `Template.yml`); блоки `<!--#if (false)-->` в `.slnx`; тестовые модули — в partial-методе `AddRepositoryModules`
   (без реализации вызов исчезает); в `Compile.yml` строку `-- test` меняет символ шаблона. Шаблонизатор не трогает
   `#if` в C# (`"*.cs": { "operations": [] }`), иначе вырезал бы весь `#if DEBUG`. README плагина —
   `template/content/README.md`. Пакет — `template/Davydov.Revit.Templates.csproj` (`PackageType Template`, `dotnet
   pack`). Ключи `AppContext` Launcher → модуль теперь с именем плагина: два плагина в одном Revit не читают чужой
   Launcher. `.github/workflows/Template.yml` (только здесь): пакет → `dotnet new` → нет заглушек и лишних файлов →
   `dotnet run -c Release --project build` в созданном проекте.
   **Сделано — 6.4, документы для агентов (2026-10-25):** в шаблоне — `AGENTS.md` (правила и указатель навыков),
   `CLAUDE.md` (`@AGENTS.md`), `ARCHITECTURE.md`, навыки `.agents/skills/` (`add-ribbon-button`, `add-command`,
   `add-page`), на английском; у репозитория — свои `AGENTS.md` и `CLAUDE.md` на русском, в шаблон не входят.
   **Сделано — 6.3, выпуск пакета шаблона (2026-10-25, решение 37):** `.github/workflows/PublishTemplate.yml` — по тегу
   `v<версия>` или вручную (dry run по умолчанию): проверки `Template.yml` (`workflow_call`), `dotnet pack` с версией из
   тега, `NuGet/login` (Trusted Publishing, пользователь `NikolaiDavydov`, окружение `nuget`), `dotnet nuget push`,
   GitHub Release с `.nupkg`. README страницы пакета — `template/README.md`.
   **Дальше:** первая публикация (`v0.1.0-preview.1`), 6.2 `PublishRelease.yml` для плагинов, 6.5 TS-типы.
7. **Опционально** — MCP, Roslyn-скрипты (переносятся из AnalyseTool как модули `AddMcp()` и т.п.).
   **Прототип MCP (2026-10-26, решение 41):** модуль `Davydov.Revit.Hosting.Mcp`, `[McpTool]` на `GetActiveDocument` и
   `CountElementsByCategory`; 20 тестов в `Hosting.Tests` через MCP-клиент SDK — обе ревизии протокола, токен, `Origin`,
   занятый порт, ошибки каталога. Проверка в Revit — `docs/manual-tests.md`, раздел MCP.
   Системы сторонних расширений нет (решение 12).

## 14. Открытые вопросы

_(открытых вопросов нет)_

## 15. Решения

| # | Решение | Дата |
| --- | --- | --- |
| 1 | ~~Наши модули сначала живут в исходниках шаблона; в NuGet выносим, когда API устоится.~~ — заменено решением 38: модули — код плагина, пути в NuGet нет. | 2026-10-24 |
| 2 | В базе только WebView2 + веб-фронтенд. MCP, Roslyn — позже, опциональными модулями (фаза 7). ~~Расширения/SDK~~ — отменено решением 12. | 2026-10-24 |
| 3 | Раздача через `dotnet new` (`.template.config`, переименование по `sourceName`). | 2026-10-24 |
| 4 | Revit 2025–2027: net8 для 2025/2026, net10 для 2027. | 2026-10-24 |
| 5 | Фронтенд — React (вместо Vue из AnalyseTool). Мост к Revit — отдельный TS-модуль без привязки к фреймворку. | 2026-10-24 |
| 6 | Сериализация — System.Text.Json везде. Newtonsoft — только внутри обёрток сторонних библиотек. ~~Версия STJ из runtime (без NuGet-пакета); поэтому версии Microsoft.Extensions.* совпадают с runtime каждого TFM.~~ — изменено решением 22: одна версия 10.x для всех, как у Nice3point. | 2026-10-24 |
| 7 | Имя команды по умолчанию = имя класса; `[Command("…")]` — необязательное переопределение. | 2026-10-24 |
| 8 | ~~События Revit — один механизм: события приложения (async), синхронные обработчики, `RevitUpdater`, пересылка во фронтенд.~~ — заменено решением 30. | 2026-10-24 |
| 9 | ~~Документ — часть запроса: `DocumentKey` из реестра, middleware `DocumentBinding`, `context.Document` вместо `ActiveUIDocument`; окна follow/pinned.~~ — заменено решением 29. | 2026-10-24 |
| 10 | Один способ: команды ~~и обработчики событий~~ (решение 30) — только классы. Регистрация — один вызов (`AddRevitCommands`), проверки при `Build()`. Делегаты в стиле Minimal API (`MapCommand`, `app.On<T>`) — не делаем, пока не понадобятся. | 2026-10-24 |
| 11 | ~~Компоненты — shadcn/ui + Tailwind 4 (не PrimeReact). Таблица — своя `<DataTable>` на TanStack Table + Virtual, собирается один раз в шаблоне.~~ Снято решением 20. | 2026-10-24 |
| 12 | Системы сторонних расширений (загрузка чужих DLL, SDK-пакет, collectible ALC) нет — ни в базе, ни опционально. | 2026-10-24 |
| 13 | ~~Регистрация команд и обработчиков событий — source generator с фазы 3, без reflection. Генератор сам пишет `AddCommands()`/`AddRevitEvents()`, ловит ошибки при компиляции (`DRT001–005`), анализатор `DRT010` запрещает `ActiveUIDocument`. DI проверяется при `Build()`.~~ Отменено решением 15. | 2026-10-24 |
| 14 | ~~Кнопки ленты — пул слотов в Launcher, вся лента описывается в `Program.cs`, внутреннее имя кнопки стабильное и не зависит от слота. Генерация пула по `[assembly: RibbonSlotPool(64)]`, `.Command<T>()`.~~ Отменено решением 22: кнопки — `ExternalCommand`, как у Nice3point. | 2026-10-24 |
| 15 | Как в AnalyseTool, без генератора: команды ~~и обработчики событий~~ (решение 30) регистрируются reflection-сканом сборок в DI, ошибки — при `Build()`. Окна — один общий `WebViewWindow`. ~~Launcher вызывает через reflection один класс фреймворка `RevitEntry.Start`, тот находит `Program.Build`. Кнопка ленты — метод в `Program.cs` (`AddButton(..., click: b => …)` с `ButtonContext`); в Launcher — неизменный рукописный файл `RibbonSlots.cs` (32 слота).~~ — отменено решением 22. | 2026-10-24 |
| 16 | Builder — настоящий Generic Host. Стандартные события `IHostApplicationLifetime` сохраняют смысл; готовность Revit (`ApplicationInitialized`) — отдельный `IRevitLifetime.Initialized`. Окружение `Development` в Debug. ~~`Host.CreateEmptyApplicationBuilder` внутри `RevitAppBuilder : IHostApplicationBuilder`; старт и остановка хоста — в thread pool.~~ — заменено решением 22: `Host.cs` по образцу Nice3point (`HostApplicationBuilder`, `DisableDefaults`); как ждать старт — проверить в фазе 1. | 2026-10-24 |
| 17 | Два интерфейса команд: `ICommand<TResponse>` (без входа) и `ICommand<TRequest, TResponse>`; «нет результата» — `Empty` (во фронтенд — `null`). Класс с обоими интерфейсами — ошибка при `Build()`. | 2026-10-24 |
| 18 | `ExceptionHandling` встроен и всегда стоит первым; свои слои — в `AddRevitCommands(c => c.Use…)` (~~`Program.cs`~~ — решение 22). ~~`DocumentBinding` встроен вторым; `DocumentKind.Any` / `DocumentKind.None`.~~ — снято решением 29. | 2026-10-24 |
| 19 | Встроенного подтверждения опасных команд (`ConfirmDestructive`) нет — ни в базе, ни опционально. Подтверждает вызывающий (страница React, кнопка). ~~`Destructive` в `[Command]` остаётся метаданными (MCP `destructiveHint` в фазе 7).~~ — снято решением 29: подсказки для MCP — в фазе 7, если понадобятся. | 2026-10-24 |
| 20 | Фронтенд в шаблоне минимальный: `revit-bridge` (TS), React-хуки поверх него (TanStack Query, `useActiveDocument`) и пустое приложение Vite + React + TS со страницей-примером. Библиотеку компонентов, таблицы, деревья, роутинг и т.п. выбирает каждый проект. | 2026-10-24 |
| 21 | Тесты — TUnit: без Revit (`Davydov.Revit.Hosting.Tests`) и внутри Revit через Nice3point.TUnit.Revit (`Davydov.Revit.Addin.Tests`). Тесты в Revit — локально или на своём раннере, не в облачном CI. Путь запуска плагина (`.addin` → Launcher → ALC → `Host`, решение 26) — Debug-панель «Dev» и ручной чек-лист. | 2026-10-24 |
| 22 | **Основа — структура и инструменты Nice3point** (эталон — `samples/MultiProjectSolution`): `Nice3point.Revit.Sdk` (конфигурации `Debug.R25…Release.R27`, `DeployAddin`, `LaunchRevit`), `Nice3point.Revit.Toolkit` (~~изоляция зависимостей вместо своего Launcher/ALC~~ — неверно, Toolkit изоляцию убрал, см. решения 25, 26; `AsyncExternalEvent` под `IRevitDispatcher`, `DockablePaneProvider`), `Nice3point.Revit.Extensions` (лента: кнопки — `ExternalCommand`; ставятся нашей обёрткой `AddButton<T>`, решения 25, 26), `Host.cs` + `ServiceDefaults`, `Nice3point.Revit.Logging`, ModularPipelines вместо NUKE, WixSharp, TUnit. Одна версия Microsoft.Extensions.\* (10.x) для всех версий Revit. Отклонение от Nice3point — CPM (`Directory.Packages.props`). **Наше — модули** `Davydov.Revit.Hosting` (команды, конвейер, ~~события~~ — решение 30, настройки) и `Davydov.Revit.Hosting.WebView` (WebView2, окна, док-панель, мост), подключаются методами `builder.AddRevitCommands()` / ~~`AddRevitEvents()`~~ / `AddWebView()`. | 2026-10-24 |
| 23 | Логи: ServiceDefaults Nice3point (Debug + журнал Revit, в Release — `Warning` и выше) **плюс** файловый лог Serilog как провайдер (`builder.Logging.AddSerilog`), `%LOCALAPPDATA%\<Tool>\logs\<RevitVersion>\log-.txt`, `Information`, ротация по дням. | 2026-10-24 |
| 24 | Раздача — свой `dotnet new davydov-revit`: копия структуры Nice3point `MultiProjectSolution` + наши модули в исходниках (решения 1, 3). Обновления Nice3point переносятся вручную сверкой с их примером; ~~переход на «их шаблон + наши NuGet-пакеты» — когда модули устоятся~~ (решение 38: модулей в NuGet не будет). | 2026-10-24 |
| 25 | ~~Изоляция зависимостей: **Revit 2026+** — штатная (`ManifestSettings` в `.addin`, как у Nice3point); **Revit 2025** — свой Launcher + `AssemblyLoadContext`.~~ — заменено решением 26. Остаётся: вход — один reflection-вызов `Davydov.Revit.Addin.Application` внутри ALC; кнопки ставятся обёрткой `AddButton<T>` / `SetAvailability<T>` и указывают на однострочные прокси в Launcher (`: ProxyCommand` / `: ProxyAvailability`), наличие прокси проверяется при старте; `IsRepackable=false`. | 2026-10-24 |
| 26 | Изоляция — **всегда свой Launcher + `AssemblyLoadContext`** на всех версиях Revit (2025–2027), как в AnalyseTool. Один `.addin` — в `Davydov.Revit.Addin.Launcher`, без `ManifestSettings`; штатная изоляция Revit не используется. Отклонение от структуры Nice3point; убрать Launcher можно, когда штатная изоляция станет надёжной. | 2026-10-24 |
| 27 | ~~Имя-заглушка плагина в шаблоне — `Davydov.Revit.Addin` (проекты `Davydov.Revit.Addin`, `.Launcher`, `.ServiceDefaults`, решение `Davydov.Revit.Addin.slnx`); при `dotnet new` заменяется на имя нового плагина. Наши модули (`Davydov.Revit.Hosting*`) имя плагина не используют.~~ — имя-заглушка остаётся; модули теперь тоже переименовываются (решение 38). | 2026-10-24 |
| 28 | Команды, документы, события и WebView **обязательны в каждом плагине** из шаблона (документ — правилом в приложении, решение 29; событий пока нет — решение 31), поэтому `Davydov.Revit.Hosting` (и позже `Davydov.Revit.Hosting.WebView`) остаётся отдельным проектом-основой, как `AnalyseTool.Core`, без параметров шаблона «с модулями / без». Рассматривали перенос в папку приложения и необязательные модули — отклонено. | 2026-10-24 |
| 29 | **Документ — правило приложения, не модуля.** В `Davydov.Revit.Hosting` нет реестра документов, `DocumentRef`, `DocumentKind`, `DocumentBinding` и `context.Document`; в запросе — общие `Headers`, модуль их не читает. В приложении: scoped-сервис `ActiveDocument` (активный документ, зафиксированный на всю команду; сменился — `document_changed`) и `DocumentCheckMiddleware` (ключ из заголовка `Document` фиксирует документ заранее). Плагин может заменить их своими. Выполнения в неактивном документе и закреплённых окон нет. `[Command]` — только имя и `Description`: `ReadOnly`, `Destructive`, `RequiresActiveDocument`, `DocumentKind` и `CommandMetadata` убраны. | 2026-10-24 |
| 30 | ~~**События Revit — дело приложения, не модуля.** Механизма событий в `Davydov.Revit.Hosting` нет: ни `AddRevitEvents`, ни `IAppEventHandler<T>` / `IRevitEventHandler<TArgs>`, ни `RevitUpdater`, ни debounce и `ForwardToClient`. Плагин подписывается сам (`+=`) по правилам раздела 6; во фронтенд публикует через `IClientEvents` модуля WebView. От модуля — только `IRevitLifetime`. Пример `ActiveDocumentEvents` — в фазе WebView. Фаза «События» убрана, фазы 5–8 стали 4–7.~~ — заменено решением 31. | 2026-10-25 |
| 31 | **Событий в шаблоне пока нет вовсе:** ни механизма в модулях, ни примера подписки в приложении (`ActiveDocumentEvents`), ни push во фронтенд (`IClientEvents`, сообщение Event протокола, `useRevitEvent`, `refetchOn`). Смену документа фронтенд узнаёт по ошибке `document_changed` и команде приложения `GetActiveDocument`. `IRevitLifetime` остаётся — это жизненный цикл, а не события. Правила подписки из опыта AnalyseTool — в истории плана (коммит cf395b0, раздел 6); вернёмся к событиям, когда понадобятся. | 2026-10-25 |
| 32 | ~~**Защита навигации WebView — по желанию, выключена по умолчанию.** `AddWebView(application, w => w.RestrictNavigation())` включает: переходы только на свой адрес (чужие `http(s)` — в системный браузер), новые окна — в системный браузер, сообщения в конвейер — только со своего origin. Без неё WebView ведёт себя как обычно. Настройки WebView (DevTools и контекстное меню только в Debug, без host objects) — всегда.~~ — заменено решением 34. | 2026-10-25 |
| 33 | **Этот репозиторий — шаблон, его релиз — NuGet-пакет шаблона**, а не MSI плагина-заглушки `Davydov.Revit.Addin`. `build/`, `install/`, `Compile.yml` здесь — содержимое шаблона, собранное и проверенное на заглушке. Выпуск (`publish`, `PublishRelease.yml`, CHANGELOG, GitHub Release с MSI) — содержимое шаблона для плагинов из него; делается и проверяется в фазе 6 вместе с релизом самого шаблона. | 2026-10-25 |
| 34 | **Защита навигации WebView — включена по умолчанию** (вместо решения 32): стартовый шаблон безопасен сразу — переходы только на свой адрес (virtual host в Release, dev-сервер в Debug), чужие `http(s)` и новые окна — в системный браузер, сообщения в конвейер — только со своего origin. Выключает `AddWebView(application, w => w.AllowAnyNavigation())`; `RestrictNavigation()` убран. | 2026-10-25 |
| 35 | **Лента и связь с Launcher — в приложении, не в модуле.** `RibbonButtons` (`AddButton<T>`, `SetAvailability<T>`), `LauncherInfo` и `DocumentAvailability` / `ProjectAvailability` — в `Davydov.Revit.Addin/Infrastructure/Ribbon/` (обвязка приложения; там же `Diagnostics/`). Это не конвейер, а связка плагина со своим Launcher: договор о ключах `AppContext` (`PluginInfo` ↔ `LauncherInfo`) лежит целиком в исходниках плагина и не разойдётся по версиям, когда модули уйдут в NuGet (решение 24). `Davydov.Revit.Hosting` — без `Nice3point.Revit.Extensions`. | 2026-10-25 |
| 36 | **Без запасной панели.** `FallbackRibbon` (кнопка «Startup error» на вкладке Add-Ins, открывавшая лог) убран. При сбое старта Launcher пишет `startup-error.txt` и пробрасывает исключение плагина; Revit сам сообщает об отказе внешнего приложения и пишет его в журнал. | 2026-10-25 |
| 37 | **Пакет шаблона — на nuget.org, через Trusted Publishing.** `dotnet new install Davydov.Revit.Templates` без настройки источников. Ключа в секретах нет: политика на nuget.org доверяет `PublishTemplate.yml` в окружении `nuget` репозитория `Nikola1Davydov/Davydov.Revit.Template`, `NuGet/login` меняет OIDC-токен GitHub на временный ключ. GitHub Packages не используем (установка оттуда требует токена). Каждая публикация — ещё и GitHub Release. | 2026-10-25 |
| 38 | **Модули — код плагина, а не зависимость.** Автор плагина полностью владеет `Hosting` и `Hosting.WebView` и меняет их под себя; вынесения в NuGet нет (вместо решения 1 и части решения 24). Поэтому при `dotnet new` они переименовываются под плагин: `Davydov.Revit.Hosting` → `<имя>.Hosting`, `<имя>.Hosting.WebView`. Изоляции это не мешает: каждый плагин грузит свои модули в свой ALC. | 2026-10-25 |
| 39 | **Решение для Visual Studio — классический `.sln`.** Окно «Новый проект» Visual Studio 18 не открывает `.slnx` шаблона (ни сам по себе, ни из `primaryOutputs`), а сохраняет поверх него своё решение без папок и с `Debug`/`Release` → `R25`; у Nice3point то же («.slnx is broken in Rider and VS»). Параметр `--solution-format` (`auto` — `.sln` при `HostIdentifier == "vs"`, иначе `.slnx`; `slnx`; `sln`). `template/content/Davydov.Revit.Addin.sln` генерирует из `.slnx` модуль `TemplateSolutionModule` (только репозиторий; CI падает, если файл отстал). Проверено в Visual Studio 18: открывается наше решение с папками и `Debug.R25…Release.R27`. | 2026-10-25 |
| 40 | **Dev-страница в док-панели (только Debug).** Кнопка «Dev page» показывает `/dev`: живой лог и запуск любой команды. Лог — четвёртый провайдер `LogBuffer` (кольцевой буфер на 1000 записей в памяти, уровни из секции `Logging`), страница опрашивает `GetLogEntries` раз в полсекунды с номером последней записи; записи самого опроса в буфер не попадают (по свойству `Command` состояния лога). Push-канала во фронтенд по-прежнему нет (решение 31). Запуск команд — форма над `Host.GetCommands` с JSON-полем и заголовком `Document`. Roslyn-REPL остаётся в фазе 7 (решение 2): пакет Roslyn тяжёлый и его загрузка в свой ALC не проверена. | 2026-10-26 |
| 41 | **MCP — внутри Revit, модуль `Davydov.Revit.Hosting.Mcp` на `HttpListener`** (#15), а не stdio-`exe` с мостом, как в AnalyseTool: без второго процесса и своего протокола моста, ставится вместе с плагином. Транспорт — `StreamableHttpServerTransport` и `McpServer` из `ModelContextProtocol.Core` (без ASP.NET Core: его в Revit нет); сессии и правила HTTP — наши (`McpHttpServer`): `initialize` → сессия, ревизия 2026-07-28 без сессий → сервер на запрос. Инструменты — только команды с `[McpTool]` (атрибут модуля, не свойство `[Command]`), вызов идёт через конвейер с источником `mcp`; ошибка команды — результат с `isError`. Безопасность: `localhost`, bearer-токен из `%LOCALAPPDATA%\<плагин>\<год>\mcp.json` (живёт между сессиями), отказ любому запросу с `Origin`. Порт: `AddMcp(application, basePort)` + (год − 2025), базовый порт свой у каждого плагина (символ шаблона `mcpBasePort`, случайный), `Mcp:Port` переопределяет; занятый порт — предупреждение. Включён в Debug, `Mcp:Enabled` иначе. Пока нет: «ручек» долгих задач (Claude Code рвёт вызов через 60 с), `tools/list_changed`, очистки простаивающих сессий. | 2026-10-26 |
