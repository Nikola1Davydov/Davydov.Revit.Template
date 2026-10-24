# Davydov.Revit.Template — план

Стартовый проект для новых Revit-плагинов. Основа — структура и инструменты **Nice3point** (Revit Templates, SDK,
Toolkit, ServiceDefaults, ModularPipelines, WixSharp, TUnit). Поверх неё — **наши модули**, перенесённые из опыта
AnalyseTool: WebView2-интерфейс, конвейер команд в стиле ASP.NET Core, привязка к документу, события Revit.

> Статус: черновик, обсуждается. Решения фиксируются в разделе «Решения», выбор, который ещё не сделан, —
> в «Открытых вопросах» (буквы — ссылки на них по тексту). Эталон структуры — `samples/MultiProjectSolution`
> в [Nice3point/RevitTemplates](https://github.com/Nice3point/RevitTemplates).

## 1. Идея

Две части (решение 22):

| Часть | Откуда | Что даёт |
| --- | --- | --- |
| **Основа** | Nice3point | `Nice3point.Revit.Sdk` (конфигурации `Debug.R25…Release.R27`, TFM, `DeployAddin`, `LaunchRevit`), `Nice3point.Revit.Toolkit` (external events, док-панели, базовые классы приложения и команд), `Nice3point.Revit.Extensions` (лента), `Host.cs` + `ServiceDefaults` (Generic Host, логирование в журнал Revit), ModularPipelines (сборка), WixSharp (установщик), TUnit + `Nice3point.TUnit.Revit` (тесты) |
| **Наши модули** | AnalyseTool + этот план | `Davydov.Revit.Hosting` — конвейер команд, документы, события, `IRevitDispatcher`, настройки, кнопки ленты; `Davydov.Revit.Hosting.WebView` — WebView2-окна и док-панель, мост к React; `MyTool.Launcher` — изоляция зависимостей на всех версиях Revit (решение 26) |

Наши модули подключаются так же, как у Nice3point подключается `AddServiceDefaults()` — методами-расширениями
`IHostApplicationBuilder`, в стиле ASP.NET Core / .NET Aspire:

| ASP.NET Core | В шаблоне |
| --- | --- |
| `WebApplication.CreateBuilder()` / `Program.cs` | `Host.cs` по образцу Nice3point: `HostApplicationBuilder` с `DisableDefaults = true` |
| `builder.AddServiceDefaults()` (Aspire) | то же: проект `MyTool.ServiceDefaults` из шаблона Nice3point |
| `builder.Services` (DI), `IOptions<T>`, `ILogger<T>` | то же, Microsoft.Extensions.* |
| Controllers + `AddControllers()` | классы команд + `builder.AddRevitCommands(...)` (reflection-скан сборок, как в AnalyseTool) |
| Middleware | конвейер команд, настраивается в `AddRevitCommands(c => c.Use…)` (раздел 5.4) |
| Model binding | payload автоматически → `TRequest` |
| Kestrel (`IServer`) | транспорты: WebView2, позже MCP |
| SignalR (`IHubContext`) | `IClientEvents.PublishAsync` — push событий во все открытые окна |
| `IHostedService`, `IHostApplicationLifetime` | то же; момент «Revit готов» — свой `IRevitLifetime.Initialized` (раздел 6) |
| `wwwroot` / SPA ClientApp | `ClientApp/` (React + Vite), dev-сервер с HMR в Debug |
| `SynchronizationContext` | `IRevitDispatcher` — единственный путь в Revit API, поверх `AsyncExternalEvent` из Toolkit |

### Как это выглядит

```csharp
// source/MyTool/Application.cs — как у Nice3point
public class Application : AsyncExternalApplication          // Toolkit; создаётся Launcher'ом внутри ALC (раздел 8)
{
    public override async Task OnStartupAsync()
    {
        try
        {
            await Host.StartAsync();
            CreateRibbon();
        }
        catch (Exception ex)
        {
            FallbackRibbon.Create(Application, ex);           // «Ошибка загрузки — открыть лог» (раздел 8)
        }
    }

    public override async Task OnShutdownAsync() => await Host.StopAsync();

    private void CreateRibbon()
    {
        var panel = Application.CreatePanel("Семейства", "MyTool");
        panel.AddButton<FamiliesCommand>("Семейства").SetLargeImage("/MyTool;component/Resources/Icons/Families32.png");
        panel.AddButton<PurgeFamiliesCommand>("Очистить").SetAvailability<ProjectAvailability>();   // наши обёртки, раздел 8
    }
}
```

```csharp
// source/MyTool/Host.cs — как у Nice3point + наши модули
public static class Host
{
    private static IHost? _host;

    public static async Task StartAsync()
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "MyTool",
            ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            DisableDefaults = true,
        });

        builder.AddServiceDefaults();                          // Nice3point: логирование, диагностика, сериализация
        builder.AddRevitCommands(c => c
            .FromAssemblyOf<Host>()                            // наше: команды, конвейер, документы (раздел 5)
            .UseCommandLogging());
        builder.AddRevitEvents();                              // наше: события Revit (раздел 6)
        builder.AddWebView(w => w
            .DockPane("Палитра", "/families-dock")            // наше: WebView2, мост, окна (раздел 9)
            .ForwardToClient<ActiveDocumentChanged>());

        builder.Services.AddSingleton<IFamiliesService, FamiliesService>();

        _host = builder.Build();                               // проверка DI и имён команд — ошибка здесь, а не при клике
        await _host.StartAsync();
    }

    public static Task StopAsync() => _host?.StopAsync() ?? Task.CompletedTask;
    public static T GetService<T>() where T : class => _host!.Services.GetRequiredService<T>();
}
```

```csharp
// source/MyTool/Features/Families/RenameFamily.cs — команда конвейера, имя = "RenameFamily"
[Command(Description = "Переименовать семейство")]
public sealed class RenameFamily(IRevitDispatcher revit, ILogger<RenameFamily> log)
    : ICommand<RenameFamilyRequest, RenameFamilyResult>
{
    public Task<RenameFamilyResult> ExecuteAsync(RenameFamilyRequest request, CommandContext context) =>
        revit.InvokeAsync(context.Document, doc => { /* транзакция */ });   // документ запроса, не «активный сейчас»
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
| статический `DocumentTracker`, трюки с `Idling` в `DockPaneHost`, ручной `SendEvent` в док-панели | **наше:** один механизм событий (раздел 6); док-панель — `DockablePaneProvider` из Toolkit |
| команды берут `ActiveUIDocument` в момент вызова | **наше:** документ — часть запроса, `context.Document` (раздел 7) |
| окна-копии `FamilyControlWindow`, `SettingsWindow`, …; окружение WebView2 в каждом; нет защиты навигации | **наше:** один `WebViewWindow`, одно окружение, allowlist навигации (раздел 9) |

Оставляем из AnalyseTool без изменений (проверено в бою):
- Протокол WebView2: Request / Response / Progress / Event с `id`; терпимость к окну, закрытому посреди команды.
- Правило «headless»: модуль `Davydov.Revit.Hosting` без WPF, проверка границ в CI (`Check-Boundaries.ps1`, раздел 3.1).
- Вертикальные срезы: команда, её сервисы и модели лежат в папке своей фичи (`Features/<Срез>/`).

## 3. Структура решения (по образцу Nice3point `MultiProjectSolution`)

```
MyTool/
├─ build/                                  ModularPipelines: compile / test / pack (installer + bundle) / publish
│  ├─ Modules/, Options/, Program.cs, appsettings.json
├─ installer/                              WixSharp: .msi per-user и per-machine
├─ source/
│  ├─ MyTool/                              приложение (Nice3point «Revit AddIn Application»)
│  │  ├─ Application.cs                    AsyncExternalApplication: Host + лента
│  │  ├─ Host.cs                           Generic Host + AddServiceDefaults + наши модули
│  │  ├─ Commands/                         кнопки ленты — ExternalCommand (как у Nice3point)
│  │  ├─ Features/<Срез>/                  команды конвейера (ICommand), их сервисы и модели (как срезы AnalyseTool)
│  │  ├─ EventHandlers/                    обработчики событий и апдейтеры
│  │  ├─ Resources/Icons/
│  │  ├─ appsettings.json
│  │  └─ ClientApp/                        минимальный React + Vite (раздел 10)
│  ├─ MyTool.ServiceDefaults/              Nice3point: логирование, диагностика, сериализация (+ наши слои конфигурации)
│  ├─ Davydov.Revit.Hosting/               НАШ модуль, headless: команды, конвейер, документы, события, IRevitDispatcher, IUserSettings
│  ├─ Davydov.Revit.Hosting.WebView/       НАШ модуль, WPF: окружение WebView2, WebViewWindow, док-панель, транспорт, revit-bridge (TS)
│  └─ MyTool.Launcher/                     все версии Revit: MyTool.addin, App.cs (свой ALC), Proxies.cs (решение 26)
├─ tests/
│  ├─ MyTool.Tests/                        TUnit + Nice3point.TUnit.Revit — внутри Revit
│  └─ Davydov.Revit.Hosting.Tests/         TUnit — без Revit
├─ .github/workflows/                      Compile.yml, PublishRelease.yml (из Nice3point)
├─ global.json                             SDK 10, Microsoft.Testing.Platform
├─ MyTool.slnx                             конфигурации Debug.R25 … Release.R27
├─ Directory.Packages.props                CPM — наше отклонение от Nice3point (решение 22); CentralPackageFloatingVersionsEnabled=true
├─ CLAUDE.md                               инструкции для ИИ-ассистентов (фаза 7)
└─ .template.config/                       template.json (свой dotnet new, решение 24)
```

Фичи можно выносить в отдельные проекты-модули (Nice3point «Revit AddIn Module»), когда приложение вырастет; по
умолчанию они в `source/MyTool/Features/`.

Два слова «команда» в одном проекте: **`Commands/`** — кнопки Revit (`ExternalCommand`), так принято у Nice3point;
**`Features/`** — команды нашего конвейера (`ICommand<…>`), так было в AnalyseTool. В WPF-коде при конфликте имени
`ICommand` с `System.Windows.Input.ICommand` — алиас `using`.

### 3.1. Зависимости проектов (проверяет `Check-Boundaries.ps1` в CI)

| Проект | Ссылается на | Правила |
| --- | --- | --- |
| `Davydov.Revit.Hosting` | Revit API (только компиляция), Nice3point.Revit.Toolkit, Microsoft.Extensions.* | **headless**: без `UseWPF`, `System.Windows`, `MessageBox` |
| `Davydov.Revit.Hosting.WebView` | Hosting, WebView2, Toolkit | единственное место WPF и диалогов наших модулей |
| `MyTool.ServiceDefaults` | Toolkit, Nice3point.Revit.Logging, Microsoft.Extensions.Hosting, Serilog (файловый лог, решение 23) | как в Nice3point |
| `MyTool.Launcher` | только Revit API; на приложение — **только** ради порядка сборки и `deps.json` | в коде Launcher нельзя использовать типы приложения и модулей: они загрузились бы в default ALC в обход изоляции |
| `MyTool` (приложение) | всё перечисленное, Nice3point.Revit.Extensions | в `Features/`, `Services/`, `EventHandlers/` — без `System.Windows` (UI — через `IUserNotifier` и фронтенд) и без `ActiveUIDocument` (раздел 7) |
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

public sealed class CommandContext
{
    public string CommandName { get; }
    public string Source { get; }                         // "webview2", "ribbon", позже "mcp"
    public CommandMetadata Command { get; }                // метаданные из [Command]
    public DocumentRef Document { get; }                   // привязанный документ (раздел 7); DocumentRef.None при DocumentKind.None
    public IProgress<ProgressInfo> Progress { get; }
    public CancellationToken CancellationToken { get; }    // отменяется и при закрытии документа
    public JsonElement RawPayload { get; }                 // для middleware; команде приходит уже TRequest
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandAttribute(string? name = null) : Attribute
{
    public string? Description { get; set; }
    public bool ReadOnly { get; set; }
    public bool Destructive { get; set; }                  // только метаданные (MCP destructiveHint, фаза 8); подтверждений нет — решение 19
    public bool RequiresActiveDocument { get; set; }       // нужен UIDocument: выделение, виды
    public DocumentKind DocumentKind { get; set; }         // Any (по умолчанию: нужен открытый документ) / Project / Family / None (документ не нужен)
}

// Вызов команды из C# — например, из кнопки ленты (раздел 8)
public interface ICommandSender
{
    void Send<TCommand>(object? payload = null);           // в конвейер, без ожидания; Source = "ribbon", документ — активный
}

// Доступ к Revit — поверх AsyncExternalEvent из Nice3point.Revit.Toolkit
public interface IRevitDispatcher
{
    Task<T> InvokeAsync<T>(DocumentRef document, Func<Document, T> work);      // основной путь
    Task<T> InvokeAsync<T>(DocumentRef document, Func<UIDocument, T> work);    // только если документ активен, иначе DocumentNotActiveException
    Task<T> InvokeAsync<T>(Func<UIApplication, T> work);                       // уровень приложения: открыть/создать документ, версия Revit
    Task InvokeBatchedAsync<TItem>(DocumentRef document, IReadOnlyList<TItem> items, int batchSize,
        Action<Document, IReadOnlyList<TItem>> work, IProgress<ProgressInfo>? progress, CancellationToken ct);
    // + перегрузки без результата
}

// Middleware
public delegate Task<object?> CommandDelegate(CommandContext context);
public interface ICommandMiddleware { Task<object?> InvokeAsync(CommandContext context, CommandDelegate next); }

// Окна WebView2 (модуль Hosting.WebView, раздел 9)
public interface IWindows
{
    void Open(string route, Action<WebViewWindowOptions>? configure = null);   // одно окно на маршрут (и документ, если Pinned)
    void ShowDockPane(string route);
}

// Уведомление пользователя (реализация — Hosting.WebView: TaskDialog / toast во фронтенде).
// Вызывается из thread pool (конвейер) — реализация сама переходит в UI-поток Revit через Dispatcher.
public interface IUserNotifier { void Error(string title, string message); void Info(string title, string message); }

// Кнопки ленты (раздел 8) — обёртки над Nice3point.Revit.Extensions
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
- Одни общие `JsonSerializerOptions` в DI, настраиваются как в ASP.NET Core: `builder.ConfigureJson(o => …)`.
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
- Следствие: `JsonSchemaExporter` (MCP-схемы, фаза 8) доступен на всех версиях Revit.
- Newtonsoft допустим только если его требует сторонняя библиотека, и только внутри её обёртки.

### 5.2. Имена команд

- По умолчанию имя команды = имя класса (`RenameFamily` → `"RenameFamily"`), атрибут `[Command]` не обязателен.
- `[Command("Name")]` — только чтобы переопределить имя (например, сохранить старое имя после переименования класса).
- Суффикс `Command` отбрасывается, как `Controller` в ASP.NET Core: `RenameFamilyCommand` → `"RenameFamily"`.
- Сравнение без учёта регистра.
- Встроенные команды модулей имеют префикс `Host.` (`Host.GetOpenDocuments`, `Host.PickFolder`, `Host.OpenExternalLink`).
  Точка в имени допустима **только** у встроенных команд, поэтому с командами приложения они не пересекаются.
- Конфликт имён (в том числе между приложением и `Host.*`) — **ошибка при `Build()`** с указанием обоих классов,
  а не тихий пропуск с записью в лог, как в AnalyseTool.
- Защита от переименований — генерация TS-типов из команд (фаза 7): переименовал класс → фронтенд не скомпилируется.

### 5.3. Регистрация — reflection, как в AnalyseTool (решение 15)

- `builder.AddRevitCommands(c => c.FromAssemblyOf<Host>())` сканирует указанные сборки (приложение и, если есть,
  проекты-модули фич), как `CommandDispatcher.RegisterBuiltIns` в AnalyseTool: не абстрактные классы с
  `ICommand<TResponse>` / `ICommand<TRequest, TResponse>` → DI (scoped) + реестр с именем и метаданными.
  `AddRevitEvents()` — то же для `IAppEventHandler<T>` / `IRevitEventHandler<TArgs>`.
- Встроенные `Host.*`-команды наших модулей регистрируются автоматически.
- `SafeGetTypes`: при `ReflectionTypeLoadException` берём загрузившиеся типы, остальное — в лог с именем
  недостающей зависимости (урок AnalyseTool).
- Правила имён (раздел 5.2) живут в одном месте — `CommandNaming` в модуле.
- **Проверки при `builder.Build()`** — до первого клика, с понятным текстом; при ошибке — запасная панель (раздел 8):

  | Проверка | Пример сообщения |
  | --- | --- |
  | одинаковые имена команд | «"GetFamilies" объявлена дважды: MyTool.Features.Families.GetFamilies и MyTool.Features.Library.GetFamiliesCommand» |
  | `TRequest` — интерфейс или абстрактный класс | «RenameFamilyRequest: System.Text.Json не сможет создать абстрактный тип» |
  | класс реализует несколько `ICommand<…>` | «PurgeFamilies реализует ICommand дважды» |
  | точка в имени команды приложения | «точка зарезервирована за Host.*» |
  | незарегистрированные зависимости | `ValidateOnBuild` (включается в ServiceDefaults, раздел 4.1): «IFamiliesService не зарегистрирован (нужен GetFamilies)» |

- Вызов: при регистрации для каждой команды один раз собирается типизированный делегат вызова, поэтому reflection не
  выполняется на каждый вызов.
- Команды только для отладки — обычный `#if DEBUG` вокруг класса.
- `ActiveUIDocument` в `Features/`, `Services/`, `EventHandlers/` запрещён — проверяет скрипт в CI (раздел 7).

### 5.4. Конвейер middleware

`ExceptionHandling` и `DocumentBinding` встроены (решение 18): модуль сам ставит их первыми — забыть или поставить не
туда нельзя. `DocumentBinding` бросает свои ошибки («документ закрыт», «документ не активен»), поэтому
`ExceptionHandling` обязан стоять перед ним. Свои слои — в `AddRevitCommands(c => c.Use…)`.

```
[встроено] ExceptionHandling → DocumentBinding → [c.Use…: CommandLogging, SingleFlight, свои] → команда
```

| # | Middleware | Что делает | Подключение |
| --- | --- | --- | --- |
| 1 | `ExceptionHandling` | любое исключение → ответ `{ error }`; если у вызова нет окна-получателя (`Source = "ribbon"`) — `IUserNotifier.Error` | встроен, всегда первым |
| 2 | `DocumentBinding` | находит документ запроса, проверяет `RequiresActiveDocument` / `DocumentKind`, кладёт в `context.Document`, отменяет токен при закрытии документа (раздел 7) | встроен, всегда вторым |
| 3 | `CommandLogging` | кто, что, откуда, сколько длилось, чем кончилось | `c.UseCommandLogging()` |
| 4 | `SingleFlight` | не даёт запустить ту же команду второй раз, пока первая не закончилась (двойной клик) | `c.UseSingleFlight()`, по желанию |
| — | свои слои | `c.Use<T>()` | между встроенными и командой |
| — | конечная точка | model binding (`JsonElement` → `TRequest`) + вызов команды | всегда последней |

Встроенного подтверждения опасных команд нет (решение 19). Если действие нужно подтвердить, это делает тот, кто его
вызывает: страница React — своим диалогом, кнопка — `TaskDialog` перед `Send<T>()`. Большинство изменений модели и так
отменяется через Ctrl+Z.

Middleware не содержит бизнес-логики и не ходит в модель: выполняется вне потока Revit, видит только `RawPayload`
и метаданные.

### 5.5. Потоки, прогресс и долгие команды

- **Транспорт сразу уводит вызов из UI-потока.** `WebMessageReceived` приходит в UI-потоке Revit (он же поток
  Revit API). Транспорт запускает конвейер в thread pool (`Task.Run`), а ответы и прогресс отправляет обратно через
  `Dispatcher` окна. Код команд и middleware **никогда** не выполняется в UI-потоке.
- **`InvokeAsync` выполняется синхронно в потоке Revit**, а это и UI-поток. Пока тело работает, окно не получает
  ни прогресс, ни что-либо ещё. Для одного долгого `InvokeAsync` прогресс бесполезен.
- **Долгая работа — порциями:** `revit.InvokeBatchedAsync(context.Document, items, batchSize: 200, (doc, batch) => …,
  context.Progress, context.CancellationToken)`. Между порциями Revit получает управление: окно показывает прогресс,
  пользователь может отменить.
- **Цена порций при записи:** транзакцию и `TransactionGroup` нужно закрыть до выхода из обработчика external event,
  поэтому запись порциями даёт **несколько шагов отмены**. Команда выбирает: одна транзакция (один шаг отмены, без
  прогресса) или порции (прогресс и отмена, несколько шагов). Для чтения порции бесплатны.
- `IRevitDispatcher` строится на `AsyncExternalEvent` из Toolkit (можно вызывать с любого потока, вызовы идут по
  очереди, есть отмена). Совет Toolkit — не вызывать `.Wait()`/`.Result` в главном потоке Revit — входит в правила
  шаблона.

## 6. События Revit (модуль `Davydov.Revit.Hosting`)

Один механизм вместо четырёх из AnalyseTool. Три вида реакций:

| Что нужно | Механизм | Где выполняется |
| --- | --- | --- |
| Отреагировать в C#: обновить кэш, записать в лог, уведомить | **события приложения** `IAppEventHandler<T>` | асинхронно, вне потока Revit; в модель — через `IRevitDispatcher` |
| Вмешаться в само событие Revit: убрать предупреждение, ответить на диалог, отменить | **синхронные обработчики** `IRevitEventHandler<TArgs>` | синхронно, внутри события Revit; должны быть быстрыми |
| Изменить модель в ответ на её изменение | **`RevitUpdater`** (обёртка над `IUpdater`) | в той же транзакции; в `DocumentChanged` Revit менять модель запрещает |

Плюс пересылка во фронтенд (`ForwardToClient`, `IClientEvents`) и жизненный цикл (`IHostApplicationLifetime` +
`IRevitLifetime`). Для разовых подавлений диалогов и ошибок внутри своей операции есть готовые scope из Toolkit
(`RevitContext.BeginDialogSuppressionScope`, `RevitApiContext.BeginFailureSuppressionScope`) — синхронный обработчик
нужен, только если реагировать надо на чужие действия.

### События приложения

Модуль сам подписывается на события Revit и отдаёт обработчикам неизменяемые записи — снимок данных,
снятый ещё в потоке Revit. `Document` наружу не уходит.

```csharp
public sealed record DocumentInfo(string Key, string Title, string PathName, bool IsFamily, bool IsLinked);

public sealed record ActiveDocumentChanged(DocumentInfo? Document);   // null — открытых документов нет
public sealed record DocumentOpened(DocumentInfo Document);
public sealed record DocumentClosed(string Key, string Title);        // Key запоминается на DocumentClosing
public sealed record DocumentSaved(DocumentInfo Document);
public sealed record SelectionChanged(DocumentInfo Document, IReadOnlyList<ElementId> ElementIds);
public sealed record ModelChanged(
    DocumentInfo Document,
    IReadOnlyList<ElementId> Added, IReadOnlyList<ElementId> Modified, IReadOnlyList<ElementId> Deleted,
    IReadOnlyList<string> Transactions,
    bool FromOurCommand);   // изменение сделала наша команда — чтобы фронтенд не обновлялся по кругу
```

- `ActiveDocumentChanged` переносит проверенную логику `DocumentTracker`: сигнал — `ViewActivated` + `DocumentClosed`
  (`DocumentOpened` срабатывает раньше, чем документ становится активным), смена вида внутри того же документа
  отсекается сравнением ключа. `Key` — это `DocumentKey` из реестра открытых документов (раздел 7).
- `DocumentClosed`: в `DocumentClosedEventArgs` есть только `DocumentId`, сам документ уже недоступен. Поэтому реестр
  на `DocumentClosing` запоминает `DocumentId → DocumentKey`, а на `DocumentClosed` выпускает событие с ключом.
- `FromOurCommand`: `IRevitDispatcher` помечает, что сейчас выполняется работа нашей команды; `DocumentChanged`,
  пришедший в это время, получает `true`.

### Регистрация

```csharp
builder.AddRevitEvents(e => e                       // reflection: все IAppEventHandler<> / IRevitEventHandler<> сборок
    .Debounce<SelectionChanged>(TimeSpan.FromMilliseconds(150))
    .Debounce<ModelChanged>(TimeSpan.FromMilliseconds(300))
    .AddUpdater<SheetNumberUpdater>());             // регистрация при старте, снятие при остановке

builder.AddWebView(w => w
    .ForwardToClient<ActiveDocumentChanged>()       // → React: useRevitEvent("ActiveDocumentChanged")
    .ForwardToClient<SelectionChanged>()
    .ForwardToClient<ModelChanged>()
    .ForwardToClient<DocumentClosed>());
```

### Обработчики

```csharp
// Асинхронный: DI через конструктор, свой scope на каждое событие
public sealed class WarnOnMassDeletion(IClientEvents client, ILogger<WarnOnMassDeletion> log)
    : IAppEventHandler<ModelChanged>
{
    public async Task HandleAsync(ModelChanged e, CancellationToken ct)
    {
        if (e.FromOurCommand || e.Deleted.Count < 50) return;
        log.LogWarning("Удалено {Count} элементов в {Doc}", e.Deleted.Count, e.Document.Title);
        await client.PublishAsync("Notification", new { level = "warning", text = $"Удалено {e.Deleted.Count}" }, ct);
    }
}

// Синхронный: внутри FailuresProcessing, в потоке Revit, без async
public sealed class SuppressDuplicateMarkWarnings : IRevitEventHandler<FailuresProcessingEventArgs>
{
    public void Handle(FailuresProcessingEventArgs e)
    {
        FailuresAccessor failures = e.GetFailuresAccessor();
        foreach (FailureMessageAccessor f in failures.GetFailureMessages())
            if (IsDuplicateMark(f)) failures.DeleteWarning(f);
    }
}

// Апдейтер: UpdaterId, регистрацию и триггеры берёт на себя модуль
public sealed class SheetNumberUpdater : RevitUpdater
{
    protected override void ConfigureTriggers(UpdaterTriggers t) =>
        t.OnAdd<ViewSheet>().OnParameterChange<ViewSheet>(BuiltInParameter.SHEET_NUMBER);

    protected override void Execute(UpdaterData data) { /* правка модели в той же транзакции */ }
}
```

### Жизненный цикл

Стандартные события Generic Host сохраняют свой смысл, момент «Revit готов» — отдельное событие. Связать
`ApplicationStarted` с `ApplicationInitialized` нельзя: `ApplicationInitialized` Revit присылает **после** выхода из
`OnStartup`, а ждать его внутри `OnStartup` — гарантированное зависание.

| Момент | Событие | Что доступно |
| --- | --- | --- |
| `OnStartupAsync`, хост запущен | `IHostApplicationLifetime.ApplicationStarted` | DI, конфигурация; лента строится сразу после; `UIApplication` и документов ещё нет |
| Revit `ApplicationInitialized` | `IRevitLifetime.Initialized` | `UIApplication` есть, можно идти в модель через `IRevitDispatcher`; уже открытые документы — в реестре (раздел 7) |
| `OnShutdownAsync` | `ApplicationStopping` → `StopAsync` → `ApplicationStopped` | отписка от событий, снятие апдейтеров |

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

`Idling` наружу не отдаётся: им пользуется только модуль (готовность док-панели).

### Как устроено внутри

```
Revit-событие (поток Revit)
  └─ RevitEventSource (hosted-сервис модуля, подписан при старте хоста — ещё внутри OnStartupAsync)
       ├─ синхронные IRevitEventHandler<TArgs> → сразу, внутри события
       └─ снимок данных → Channel → фоновая обработка
              ├─ debounce / склейка (SelectionChanged, ModelChanged)
              ├─ IAppEventHandler<T> — каждый в своём DI-scope
              └─ ForwardToClient → IClientEvents → все открытые окна и док-панели
```

Правила:
- Подписка только при старте, в валидном контексте API — в обработчике WPF `Loaded` Revit бросает исключение (урок
  `DockPaneHost`). `RevitEventSource.StartAsync` выполняется внутри `OnStartupAsync`, а `AsyncExternalApplication`
  возвращает продолжения в поток Revit (раздел 4.1) — подписка там допустима.
- На «горячие» `DocumentChanged` и `SelectionChanged` подписываемся, только если есть хоть один обработчик или пересылка.
- Ни одно исключение из обработчика не уходит в Revit: ловится и логируется; ошибка одного обработчика не мешает остальным.
- При остановке хоста всё отписывается, апдейтеры снимаются.

## 7. Документы: пользователь может сменить активный документ в любой момент

### Что может пойти не так

1. **Изменение уходит не в тот документ.** Окно показывает данные документа A, пользователь переключился на B и нажал
   «Переименовать». Команда берёт `ActiveUIDocument` (= B) и делает `GetElement(id)` с `ElementId` из A.
   `ElementId` уникален только внутри документа — в B может тихо измениться другой элемент.
2. **Долгая команда разъезжается между документами** — между двумя `InvokeAsync` пользователь переключился.
3. **Документ закрыли** во время команды или пока окно было открыто.
4. **Открылся редактор семейств** — активным стал документ семейства, команда рассчитана на проект.
5. **События из неактивного документа** — `DocumentChanged` приходит для любого документа, окна обновляются зря.

### Решение: документ — часть запроса

Аналогия из ASP.NET Core — мультитенантность: запрос несёт tenant (`X-Tenant`), middleware его определяет,
дальше весь запрос работает только с ним. Здесь tenant — документ.

1. **Реестр `IDocumentRegistry`** выдаёт каждому открытому документу `DocumentKey`: при `DocumentOpened`/`DocumentCreated`
   (и для уже открытых — на `ApplicationInitialized`), удаляет при `DocumentClosed` (ключ для события — см. раздел 6).
   Ключ не меняется после «Сохранить как» и не путает две копии с одинаковым именем (в отличие от `PathName|Title`).
   Связанные файлы — с пометкой `IsLinked`, менять их нельзя.
2. **Каждый запрос несёт документ.** Фронтенд добавляет его сам:
   `{"type":"request","id":"7","command":"RenameFamily","document":"d3f1","payload":{…}}`.
   Без документа (лента, MCP) — берётся активный **в момент вызова** и фиксируется на всю команду.
3. **Middleware `DocumentBinding`** (встроен, раздел 5.4): находит документ по ключу (закрыт → ошибка `DocumentClosed`),
   проверяет требования команды, кладёт документ в `context.Document`, отменяет `context.CancellationToken` при
   `DocumentClosing` этого документа.
4. **Команда работает только с привязанным документом:** `revit.InvokeAsync(context.Document, doc => …)` находит
   документ заново в потоке Revit при каждом вызове — все шаги долгой команды идут в один документ; закрыт →
   `DocumentClosedException`.
5. **Требования — в атрибуте:**

   ```csharp
   [Command(RequiresActiveDocument = true)]       // выделение, виды, ShowElements — нужен UIDocument
   public sealed class SelectElements(IRevitDispatcher revit) : ICommand<SelectElementsRequest, Empty>
   {
       public Task<Empty> ExecuteAsync(SelectElementsRequest request, CommandContext context) =>
           revit.InvokeAsync(context.Document, (UIDocument ui) =>
           {
               ui.Selection.SetElementIds(request.Ids);
               return Empty.Value;
           });
   }

   [Command(DocumentKind = DocumentKind.Project)]  // в редакторе семейств — понятная ошибка
   public sealed class GetFamilies …

   [Command(DocumentKind = DocumentKind.None)]     // документ не нужен: работает и без открытых документов
   public sealed class GetSettings …               // так же Ping, Host.GetOpenDocuments, Host.PickFolder
   ```

   По умолчанию (`DocumentKind.Any`) команде нужен открытый документ: таких команд большинство. Нет открытых
   документов → понятная ошибка «Откройте документ», команда не запускается.

   Изменять модель можно и в неактивном документе (Revit API это позволяет, и пользователь смотрел именно на данные A).
   Действия с интерфейсом — только в активном, иначе отказ.
6. **Защита в CI:** скрипт (рядом с `Check-Boundaries.ps1`) запрещает `ActiveUIDocument` в `Features/`, `Services/`
   и `EventHandlers/`, в том числе внутри `InvokeAsync(Func<UIApplication, T>)` — эта перегрузка для действий уровня
   приложения, а не для обхода привязки. Исключение — строка с комментарием `// AllowActiveDocument: причина`.
   В кнопках (`Commands/`, раздел 8) активный документ использовать можно: клик и относится к нему.

### Фронтенд: каждое окно знает свой документ

```tsx
<RevitDocumentProvider mode="follow">   {/* док-панель: следует за активным документом */}
  <FamiliesPage />
</RevitDocumentProvider>

<RevitDocumentProvider mode="pinned">   {/* окно с ленты: привязано к документу, из которого открыто */}
  <FamiliesPage />
</RevitDocumentProvider>
```

- Страницы не меняются: `useCommand("GetFamilies")` сам добавляет `document` в запрос, ключ кэша —
  `[documentKey, "GetFamilies"]`. Вернулся в A — данные A мгновенно из кэша.
- `refetchOn: ["ModelChanged"]` реагирует только на события своего документа.
- Привязанное окно показывает документ в заголовке; пришёл `DocumentClosed` с его ключом — плашка «Документ закрыт»,
  кнопки неактивны.
- С ленты: `Host.GetService<IWindows>().Open("/families", o => o.Pinned = true)` (раздел 8).

### Сценарии

| Ситуация | Поведение |
| --- | --- |
| Переключился A → B и сразу нажал кнопку в окне с данными A | команда выполняется в A — как пользователь видел |
| Переключился посреди долгой команды | команда доделывает работу в своём документе |
| Закрыл документ во время команды | команда отменяется, в окне «Документ закрыт» |
| Док-панель, переключение A → B | перезапрос данных B; возврат в A — из кэша |
| Открыл редактор семейств | команды «только проект» отвечают понятной ошибкой |
| Вызов с ленты или из MCP без документа | активный на момент вызова, зафиксирован |
| Выделить элементы в неактивном документе | отказ: «Документ не активен» |

## 8. Лента и вход из Revit — как у Nice3point (решение 22)

### Вход и изоляция (решение 26)

Своей изоляции в текущем Toolkit нет: в `CHANGELOG.md` Toolkit сказано, что `AssemblyLoadContext` удалён и изоляция
переезжает в сам Revit. Базовые классы `ExternalApplication` / `ExternalCommand` только ищут зависимости в папке
плагина (`ResolveHelper`), если сборка загружена в обычный контекст. Штатная изоляция Revit 2026+ (`ManifestSettings`)
по сообщениям на форуме Autodesk в 2026.1 работает ненадёжно, а в Revit 2025 её нет вовсе. Поэтому изоляция — **своя,
одинаковая на всех версиях**, как в AnalyseTool:

```
Revit 2025–2027 → MyTool.addin (в проекте MyTool.Launcher) → MyTool.Launcher.App
  ├─ свой AssemblyLoadContext + AssemblyDependencyResolver, как IsolatedAssemblyLoadContent в AnalyseTool
  ├─ в нём — MyTool.dll и все зависимости (Microsoft.Extensions.*, WebView2, Toolkit); Revit API — общий
  ├─ reflection — ОДИН раз: создать MyTool.Application внутри ALC и вызвать его OnStartup / OnShutdown
  │   (Toolkit в чужом ALC сам отключает ResolveHelper — проверено по исходникам)
  ├─ MyTool.Application.OnStartupAsync: Host.StartAsync() → CreateRibbon()
  └─ ошибка на любом шаге → запасная панель «Ошибка загрузки — открыть лог» без ALC (урок BugsCommand)
```

- Манифест один — в Launcher, без `ManifestSettings`: Launcher без зависимостей грузится в контекст Revit, всё
  остальное живёт в нашем ALC. `DeployAddin` включён в Launcher (SDK требует его в проекте с манифестом); Launcher
  ссылается на приложение ради порядка сборки и `deps.json`, поэтому в папку плагина попадает всё.
- Имя класса приложения (`MyTool.Application`) Launcher берёт из константы, которую шаблон подставляет при создании
  проекта. Если класс не найден — запасная панель с понятным текстом, а не пустая лента.
- Это отклонение от структуры Nice3point (у них манифест в приложении с `ManifestSettings`). Если штатная изоляция
  Revit когда-нибудь станет надёжной, Launcher можно убрать: приложение и кнопки уже написаны в стиле Nice3point.

### Кнопки — классы `ExternalCommand` в `Commands/`

```csharp
// source/MyTool/Commands/PurgeFamiliesCommand.cs
[Transaction(TransactionMode.Manual)]
public class PurgeFamiliesCommand : ExternalCommand
{
    public override void Execute() => Host.GetService<ICommandSender>().Send<PurgeFamilies>();
}

// source/MyTool/Commands/FamiliesCommand.cs
[Transaction(TransactionMode.Manual)]
public class FamiliesCommand : ExternalCommand
{
    public override void Execute() =>
        Host.GetService<IWindows>().Open("/families", o => { o.Width = 1100; o.Height = 720; o.Pinned = true; });
}
```

```csharp
// Application.CreateRibbon — Nice3point.Revit.Extensions + наши обёртки AddButton / SetAvailability
var panel = Application.CreatePanel("Семейства", "MyTool");
panel.AddButton<FamiliesCommand>("Семейства")
    .SetImage("/MyTool;component/Resources/Icons/Families16.png")
    .SetLargeImage("/MyTool;component/Resources/Icons/Families32.png");
panel.AddButton<PurgeFamiliesCommand>("Очистить")
    .SetAvailability<ProjectAvailability>();               // IExternalCommandAvailability

#if DEBUG
var dev = Application.CreatePanel("Dev", "MyTool");        // Debug-панель для проверки команд (раздел 12)
dev.AddButton<PingCommand>("Ping");
#endif
```

```csharp
// source/MyTool.Launcher/Proxies.cs — по строке на кнопку и на класс доступности
public sealed class FamiliesCommand : ProxyCommand;
public sealed class PurgeFamiliesCommand : ProxyCommand;
public sealed class PingCommand : ProxyCommand;
public sealed class ProjectAvailability : ProxyAvailability;

// ProxyCommand (пишется один раз): находит в ALC класс с ТЕМ ЖЕ именем, создаёт и вызывает Execute
```

Почему прокси: Revit создаёт класс кнопки сам по пути к DLL и имени типа. `AddPushButton<T>` из Extensions берёт путь
сборки `T` — Revit загрузил бы `MyTool.dll` второй раз, в обычный контекст, в обход Launcher (и со своей пустой копией
`Host`). Обёртка `AddButton<T>` подставляет путь Launcher и имя прокси, а при старте проверяет, что прокси есть: если
нет — ошибка «Добавь в MyTool.Launcher/Proxies.cs: `public sealed class PurgeFamiliesCommand : ProxyCommand;`».

Правила:
- **Кнопка тонкая.** Она открывает окно (`IWindows`), показывает док-панель или отправляет команду конвейера
  (`ICommandSender.Send<T>()`). Логика — в командах (`Features/`) и сервисах.
- **`Send<T>()` не ждёт выполнения.** Вызов уходит в конвейер (`Source = "ribbon"`, документ — активный на момент
  клика) **без ожидания**: команда сама пойдёт через external event и ждала бы событие, внутри которого стоит клик
  (урок `InvokeSavedCommand` из AnalyseTool). Ошибки — через `IUserNotifier`.
- **Окна.** `IWindows.Open(route)` открывает общий `WebViewWindow` (раздел 9); второй клик поднимает уже открытое окно
  этого маршрута (и документа, если `Pinned`), как `_familyWindow` / `Restore` в `RibbonHost`.
- **Стабильный id кнопки.** По внутреннему имени кнопки Revit привязывает горячие клавиши, панель быстрого доступа и
  `PostCommand`. `AddPushButton<T>` берёт имя `T.FullName` — **вместе с пространством имён**: переименование класса и
  перенос в другую папку или namespace сбрасывают привязки пользователя. Один класс кнопки — одна кнопка в панели.
- **Доступность** — обычные `IExternalCommandAvailability` (`ProjectAvailability`, `DocumentAvailability` — в модуле),
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
  (`Title`, `Width`, `Height`, `Pinned`): `Owner` = главное окно Revit, общее окружение, транспорт `Attach`/`Detach`,
  переход на маршрут, одно окно на маршрут и документ. Особое поведение — опциями, в крайнем случае наследованием.
  Тот же контент — в док-панели (раздел 8).
- **Контент:** Release — `SetVirtualHostNameToFolderMapping("app.<tool>.local", dist)`; Debug — dev-сервер Vite
  (`ClientApp:DevServerUrl` в настройках, по умолчанию `http://localhost:5173`).
- **Безопасность.** Любая страница, загруженная в WebView, может вызывать команды через `postMessage` — значит,
  одна внешняя ссылка (например, в changelog) отдаст модель чужому сайту. Поэтому:
  - `NavigationStarting` — отменять переход на любой адрес, кроме своего virtual host (и dev-сервера в Debug);
  - `NewWindowRequested` — `Handled = true`, `http(s)`-ссылки открывать в системном браузере;
  - `WebMessageReceived` — проверять `args.Source`: сообщения не с нашего origin игнорировать и логировать;
  - Release: `AreDevToolsEnabled = false`, `AreHostObjectsAllowed = false`, стандартное контекстное меню выключено.
- **Устойчивость:** отправка в окно, закрытое посреди команды, не бросает исключений (как в `WebView2Transport`).

## 10. Фронтенд

**Фронтенд в шаблоне минимальный (решение 20).** Шаблон отвечает только за связь фронтенда с Revit — то, что
завязано на протокол и возможности наших модулей. Внешний вид, компоненты и библиотеки интерфейса выбирает каждый
проект.

**Входит в шаблон:**

1. **`revit-bridge`** — чистый TypeScript, без зависимостей от React; живёт в модуле `Davydov.Revit.Hosting.WebView`
   и подключается в `ClientApp` локальным npm-пакетом. Переносится из `RevitBridge.ts` AnalyseTool:
   `invoke(command, payload, { onProgress, signal })`, подписка на события (`on("ActiveDocumentChanged", …)`),
   сопоставление Request/Response по `id`, документ запроса (раздел 7). В Debug без Revit (в обычном браузере) —
   mock-режим. Тесты — Vitest.
2. **React-хуки поверх моста** — `useCommand` / `useCommandMutation` (на TanStack Query: кэш по документу, состояние
   загрузки, прогресс), `useRevitEvent`, `refetchOn`, `RevitDocumentProvider` (follow/pinned).
3. **Пустое приложение Vite + React + TypeScript** (`source/MyTool/ClientApp`) с одной страницей-примером: вызов команды
   с прогрессом, подписка на смену документа. Сборка в `dist`, который отдаёт `WebViewWindow` (раздел 9), dev-сервер с
   HMR в Debug. В поставку `dist` попадает как `<Content Include="ClientApp/dist/**" CopyToPublishDirectory="PreserveNewest"/>`
   (так SDK Nice3point добавляет файлы в publish и deploy); `npm run build` — шаг перед publish в ModularPipelines.

**Не входит в шаблон:** библиотека компонентов, таблицы, деревья, роутинг, состояние, графики — проект подключает
сам, что ему нужно.

Пример вызова команды и подписки на события:

```tsx
const families = useCommand<FamilyDto[]>("GetFamilies", {
  refetchOn: ["ActiveDocumentChanged", "ModelChanged"],     // перезапросить при смене документа / правке модели
});
const rename = useCommandMutation("RenameFamily");

useRevitEvent<SelectionChanged>("SelectionChanged", e => setSelectedIds(e.elementIds));

if (families.isLoading) return <Spinner />;
return <FamiliesTable data={families.data} onRename={r => rename.mutate(r)} />;   // компонент проекта, не шаблона
```

Позже (фаза 7): типы `TRequest`/`TResponse` и событий для TypeScript генерировать из C#, чтобы фронтенд и бэкенд не расходились.

## 11. Настройки и логи (`MyTool.ServiceDefaults` + модуль)

- **ServiceDefaults — как у Nice3point:** `AddLoggingDefaults` (секция `Logging`, провайдер Debug, журнал Revit через
  `Nice3point.Revit.Logging` с уровнем `Error`), `AppDomainExceptionsHandler` (необработанные исключения — в лог),
  `JsonSerializerContext`. Это проект приложения, его можно менять.
- **Наши правки в ServiceDefaults:** `ValidateOnBuild` / `ValidateScopes` (раздел 4.1), `DefaultJsonTypeInfoResolver`
  в цепочке JSON (раздел 5.1), слои конфигурации и файловый лог (ниже).
- **Наше дополнение в ServiceDefaults — слои конфигурации** (позднее перекрывает раннее):
  1. `appsettings.json` рядом с DLL — значения по умолчанию, только чтение;
  2. `appsettings.{Environment}.json` — `Development` в Debug (например, адрес dev-сервера и подробные логи);
  3. `%AppData%\<Tool>\<RevitVersion>\usersettings.json` — то, что пользователь поменял в интерфейсе;
  4. переменные окружения `MYTOOL_…` — для отладки.
- **Чтение** — обычные `IOptions<T>` / `IOptionsMonitor<T>` (`reloadOnChange` для пользовательского файла).
- **Запись** — `IUserSettings` (модуль `Davydov.Revit.Hosting`): `await settings.UpdateAsync<AiOptions>("Ai", o => o.Model = "…")`
  пишет секцию в `usersettings.json`, `IOptionsMonitor` подхватывает изменение. Так переносятся `mcp.json` и
  настройки AI-провайдеров из AnalyseTool. Секреты (API-ключи) — не в JSON, а через DPAPI (`ProtectedData`).
- **Журнал Revit:** в Release — только `Warning` и выше (журнал хранит всю сессию и уходит в Autodesk с отчётом об
  ошибке); секреты и лишние личные данные туда не пишутся.
- **Файловый лог (решение 23)** — наше дополнение в ServiceDefaults: Serilog **как провайдер**
  (`builder.Logging.AddSerilog(...)`, не `UseSerilog()`, иначе журнал Revit перестанет получать записи), файл
  `%LOCALAPPDATA%\<Tool>\logs\<RevitVersion>\log-.txt`, уровень `Information`, ротация по дням. Каждая команда
  логируется с `CommandName`, `Source`, `DocumentKey` (через `CommandLogging`). Журнал Revit — контекст сбоя рядом с
  действиями пользователя, файл — подробности работы плагина для поддержки.

## 12. Тесты и цикл разработки

Один тестовый фреймворк — **TUnit** — и для тестов без Revit, и для тестов внутри Revit (решение 21), как в шаблонах
Nice3point (`global.json` → Microsoft.Testing.Platform).

- **Unit-тесты без Revit** (`tests/Davydov.Revit.Hosting.Tests`): конвейер, middleware, model binding, логика
  `DocumentBinding`, реестр команд (правила имён, проверки `Build()`) — с фейковыми `IRevitDispatcher` /
  `IDocumentRegistry`. Типы Revit вне Revit создать нельзя, поэтому логика, которую хотим тестировать, не трогает их
  напрямую.
- **Тесты внутри Revit** (`tests/MyTool.Tests`, Nice3point «Revit Test») —
  [Nice3point.TUnit.Revit](https://github.com/Nice3point/RevitUnit): тесты выполняются в контексте Revit API, DI —
  через `DependencyInjectionDataSourceAttribute`, как в `MultiProjectSolution`.

  ```csharp
  public sealed class DispatcherTests : RevitApiTest          // каждый тест — в однопоточном контексте Revit API
  {
      private Document _doc = null!;

      [Before(Test)] public void Open()  => _doc = Application.NewProjectDocument(UnitSystem.Metric);
      [After(Test)]  public void Close() => _doc.Close(false);

      [Test]
      public async Task Registry_sees_new_document() { /* … */ }
  }
  ```

  ```
  dotnet test -c "Release.R26"      # или dotnet run; конфигурация выбирает версию Revit
  ```

  - Что проверяют: `IRevitDispatcher`, `IDocumentRegistry`, команды, которые меняют модель, `RevitUpdater`, события
    с реальными документами. `RevitApiUiTest` — для того, что требует `UIApplication`.
  - Нужен установленный и лицензированный Revit той версии, что в конфигурации. Поэтому в облачном CI эти тесты не
    запускаются — локально или на своём раннере с Revit (фаза 6).
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
   `Host.cs` + `ServiceDefaults` (с `ValidateOnBuild` и JSON-резолвером), `MyTool.Launcher` с `.addin`, своим ALC и
   прокси, обёртки `AddButton<T>` / `SetAvailability<T>`, одна кнопка, запасная панель, `DeployAddin`/`LaunchRevit`
   в Launcher. Из `.csproj` Nice3point переносится `EnableDynamicLoading=true`; `IsRepackable=false` (изоляция есть).
   Проверить: Launcher и ALC рядом с AnalyseTool на 2025, 2026 и 2027 (раздел 8), WebView2 в изолированном контексте,
   Hot Reload.
   Готово, когда: кнопка на ленте пишет в журнал Revit на 2025, 2026 и 2027; при исключении в `Host.StartAsync`
   появляется запасная панель; плагин работает рядом с AnalyseTool; без прокси в Launcher кнопка даёт понятную
   ошибку при старте.
2. **Модуль `Davydov.Revit.Hosting`, основа** — `IRevitDispatcher` поверх `AsyncExternalEvent` (все перегрузки,
   включая `InvokeBatchedAsync`), `IRevitLifetime`, слои конфигурации в ServiceDefaults и `IUserSettings`,
   `IUserNotifier`, `ICommandSender`, окружение `Development`/`Production`.
   Готово, когда: сервис из DI читает документ через `IRevitDispatcher` после `IRevitLifetime.Initialized`;
   `BackgroundService` с обычным `await` не вешает запуск Revit; настройка, записанная через `IUserSettings`,
   переживает перезапуск Revit; измерено время старта хоста.
3. **Команды** — `AddRevitCommands` (reflection-регистрация и проверки `Build()`, раздел 5.3), встроенные
   `Host.*`-команды, конвейер с встроенными middleware (раздел 5.4), запуск конвейера в thread pool,
   `IDocumentRegistry`, `DocumentBinding`, `context.Document`, Debug-панель «Dev».
   Готово, когда: команда с DI и `TRequest` вызывается с ленты и проходит через middleware; команда с документом A
   выполняется в A при активном B; ошибка команды с ленты показывается через `IUserNotifier`; есть unit-тесты на
   конвейер и тесты внутри Revit (Nice3point.TUnit.Revit) на `IRevitDispatcher` и `IDocumentRegistry`.
4. **События** — `AddRevitEvents`: `RevitEventSource`, события приложения (включая `DocumentClosed` с ключом),
   синхронные обработчики, `RevitUpdater`, debounce.
   Готово, когда: смена документа и правка модели доходят до `IAppEventHandler`, апдейтер срабатывает,
   исключение в обработчике не роняет Revit.
5. **Модуль `Davydov.Revit.Hosting.WebView`** — окружение WebView2 и защита навигации (раздел 9), `WebViewWindow`,
   док-панель через `DockablePaneProvider`, транспорт, `revit-bridge` (TS), React-хуки, пустое React-приложение со
   страницей-примером, Vite HMR в Debug, `ForwardToClient` / `useRevitEvent` / `refetchOn`, `RevitDocumentProvider`.
   Готово, когда: кнопка в React вызывает команду и показывает результат и прогресс (на `InvokeBatchedAsync`);
   при смене документа док-панель сама обновляется, а привязанное окно остаётся на своём документе; переход по
   внешней ссылке открывает системный браузер.
6. **Сборка и выпуск — как у Nice3point** — ModularPipelines (`build/`: compile, test, pack, publish), WixSharp
   (`installer/`) + bundle для Autodesk Store, GitHub workflows `Compile.yml` / `PublishRelease.yml`, версия из
   git-тегов, CHANGELOG. Плюс наше: проверка границ по таблице 3.1 и запрета `ActiveUIDocument` (раздел 7), сборка
   фронтенда в `pack`. Тесты внутри Revit — не в облачном CI: локально или на своём раннере.
7. **Шаблон** — свой `dotnet new davydov-revit -n MyTool` (решение 24): копия структуры Nice3point + наши модули в
   исходниках, параметры; в README шаблона — с какой версией примера Nice3point он сверен и как переносить их обновления.
   **Каждый новый проект получает свои идентификаторы** (`guids` и `sourceName` в `template.json`): `AddInId` в `.addin`,
   имя класса приложения для Launcher, имя ALC, GUID док-панели, UpgradeCode установщика (`build/appsettings.json`), имена папок WebView2,
   логов и `usersettings`, имя virtual host. Проверка: два плагина из шаблона одновременно в одном Revit. CLAUDE.md и навыки для ИИ-агентов
   (как `.agents/skills` у Nice3point: правила документов, команд, событий, потоков, ленты); генерация TS-типов
   (утилита после сборки читает команды из DLL через `MetadataLoadContext` и пишет `ClientApp/src/generated/commands.ts`).
8. **Опционально** — MCP, Roslyn-скрипты (переносятся из AnalyseTool как модули `AddMcp()` и т.п.).
   Системы сторонних расширений нет (решение 12).

## 14. Открытые вопросы

_(открытых вопросов нет)_

## 15. Решения

| # | Решение | Дата |
| --- | --- | --- |
| 1 | Наши модули сначала живут в исходниках шаблона; в NuGet выносим, когда API устоится. | 2026-10-24 |
| 2 | В базе только WebView2 + веб-фронтенд. MCP, Roslyn — позже, опциональными модулями (фаза 8). ~~Расширения/SDK~~ — отменено решением 12. | 2026-10-24 |
| 3 | Раздача через `dotnet new` (`.template.config`, переименование по `sourceName`). | 2026-10-24 |
| 4 | Revit 2025–2027: net8 для 2025/2026, net10 для 2027. | 2026-10-24 |
| 5 | Фронтенд — React (вместо Vue из AnalyseTool). Мост к Revit — отдельный TS-модуль без привязки к фреймворку. | 2026-10-24 |
| 6 | Сериализация — System.Text.Json везде. Newtonsoft — только внутри обёрток сторонних библиотек. ~~Версия STJ из runtime (без NuGet-пакета); поэтому версии Microsoft.Extensions.* совпадают с runtime каждого TFM.~~ — изменено решением 22: одна версия 10.x для всех, как у Nice3point. | 2026-10-24 |
| 7 | Имя команды по умолчанию = имя класса; `[Command("…")]` — необязательное переопределение. | 2026-10-24 |
| 8 | События Revit — один механизм: события приложения (async), синхронные обработчики, `RevitUpdater`, пересылка во фронтенд. | 2026-10-24 |
| 9 | Документ — часть запроса: `DocumentKey` из реестра, middleware `DocumentBinding`, `context.Document` вместо `ActiveUIDocument`; окна follow/pinned. | 2026-10-24 |
| 10 | Один способ: команды и обработчики событий — только классы. Регистрация — один вызов (`AddRevitCommands`), проверки при `Build()`. Делегаты в стиле Minimal API (`MapCommand`, `app.On<T>`) — не делаем, пока не понадобятся. | 2026-10-24 |
| 11 | ~~Компоненты — shadcn/ui + Tailwind 4 (не PrimeReact). Таблица — своя `<DataTable>` на TanStack Table + Virtual, собирается один раз в шаблоне.~~ Снято решением 20. | 2026-10-24 |
| 12 | Системы сторонних расширений (загрузка чужих DLL, SDK-пакет, collectible ALC) нет — ни в базе, ни опционально. | 2026-10-24 |
| 13 | ~~Регистрация команд и обработчиков событий — source generator с фазы 3, без reflection. Генератор сам пишет `AddCommands()`/`AddRevitEvents()`, ловит ошибки при компиляции (`DRT001–005`), анализатор `DRT010` запрещает `ActiveUIDocument`. DI проверяется при `Build()`.~~ Отменено решением 15. | 2026-10-24 |
| 14 | ~~Кнопки ленты — пул слотов в Launcher, вся лента описывается в `Program.cs`, внутреннее имя кнопки стабильное и не зависит от слота. Генерация пула по `[assembly: RibbonSlotPool(64)]`, `.Command<T>()`.~~ Отменено решением 22: кнопки — `ExternalCommand`, как у Nice3point. | 2026-10-24 |
| 15 | Как в AnalyseTool, без генератора: команды и обработчики событий регистрируются reflection-сканом сборок в DI, ошибки — при `Build()`. Окна — один общий `WebViewWindow`. ~~Launcher вызывает через reflection один класс фреймворка `RevitEntry.Start`, тот находит `Program.Build`. Кнопка ленты — метод в `Program.cs` (`AddButton(..., click: b => …)` с `ButtonContext`); в Launcher — неизменный рукописный файл `RibbonSlots.cs` (32 слота).~~ — отменено решением 22. | 2026-10-24 |
| 16 | Builder — настоящий Generic Host. Стандартные события `IHostApplicationLifetime` сохраняют смысл; готовность Revit (`ApplicationInitialized`) — отдельный `IRevitLifetime.Initialized`. Окружение `Development` в Debug. ~~`Host.CreateEmptyApplicationBuilder` внутри `RevitAppBuilder : IHostApplicationBuilder`; старт и остановка хоста — в thread pool.~~ — заменено решением 22: `Host.cs` по образцу Nice3point (`HostApplicationBuilder`, `DisableDefaults`); как ждать старт — проверить в фазе 1. | 2026-10-24 |
| 17 | Два интерфейса команд: `ICommand<TResponse>` (без входа) и `ICommand<TRequest, TResponse>`; «нет результата» — `Empty` (во фронтенд — `null`). Класс с обоими интерфейсами — ошибка при `Build()`. | 2026-10-24 |
| 18 | `ExceptionHandling` и `DocumentBinding` встроены и всегда стоят первыми; свои слои — в `AddRevitCommands(c => c.Use…)` (~~`Program.cs`~~ — решение 22). По умолчанию (`DocumentKind.Any`) команде нужен открытый документ; `DocumentKind.None` — для команд без документа (`context.Document` = `DocumentRef.None`). | 2026-10-24 |
| 19 | Встроенного подтверждения опасных команд (`ConfirmDestructive`) нет — ни в базе, ни опционально. Подтверждает вызывающий (страница React, кнопка). `Destructive` в `[Command]` остаётся метаданными (MCP `destructiveHint` в фазе 8). | 2026-10-24 |
| 20 | Фронтенд в шаблоне минимальный: `revit-bridge` (TS), React-хуки поверх него (TanStack Query, `RevitDocumentProvider`) и пустое приложение Vite + React + TS со страницей-примером. Библиотеку компонентов, таблицы, деревья, роутинг и т.п. выбирает каждый проект. | 2026-10-24 |
| 21 | Тесты — TUnit: без Revit (`Davydov.Revit.Hosting.Tests`) и внутри Revit через Nice3point.TUnit.Revit (`MyTool.Tests`). Тесты в Revit — локально или на своём раннере, не в облачном CI. Путь запуска плагина (~~Launcher → ALC~~ `.addin` → изоляция Toolkit → `Host`) — Debug-панель «Dev» и ручной чек-лист. | 2026-10-24 |
| 22 | **Основа — структура и инструменты Nice3point** (эталон — `samples/MultiProjectSolution`): `Nice3point.Revit.Sdk` (конфигурации `Debug.R25…Release.R27`, `DeployAddin`, `LaunchRevit`), `Nice3point.Revit.Toolkit` (~~изоляция зависимостей вместо своего Launcher/ALC~~ — неверно, Toolkit изоляцию убрал, см. решения 25, 26; `AsyncExternalEvent` под `IRevitDispatcher`, `DockablePaneProvider`), `Nice3point.Revit.Extensions` (лента: кнопки — `ExternalCommand`; ставятся нашей обёрткой `AddButton<T>`, решения 25, 26), `Host.cs` + `ServiceDefaults`, `Nice3point.Revit.Logging`, ModularPipelines вместо NUKE, WixSharp, TUnit. Одна версия Microsoft.Extensions.\* (10.x) для всех версий Revit. Отклонение от Nice3point — CPM (`Directory.Packages.props`). **Наше — модули** `Davydov.Revit.Hosting` (команды, конвейер, документы, события, настройки) и `Davydov.Revit.Hosting.WebView` (WebView2, окна, док-панель, мост), подключаются методами `builder.AddRevitCommands()` / `AddRevitEvents()` / `AddWebView()`. | 2026-10-24 |
| 23 | Логи: ServiceDefaults Nice3point (Debug + журнал Revit, в Release — `Warning` и выше) **плюс** файловый лог Serilog как провайдер (`builder.Logging.AddSerilog`), `%LOCALAPPDATA%\<Tool>\logs\<RevitVersion>\log-.txt`, `Information`, ротация по дням. | 2026-10-24 |
| 24 | Раздача — свой `dotnet new davydov-revit`: копия структуры Nice3point `MultiProjectSolution` + наши модули в исходниках (решения 1, 3). Обновления Nice3point переносятся вручную сверкой с их примером; переход на «их шаблон + наши NuGet-пакеты» — когда модули устоятся. | 2026-10-24 |
| 25 | ~~Изоляция зависимостей: **Revit 2026+** — штатная (`ManifestSettings` в `.addin`, как у Nice3point); **Revit 2025** — свой Launcher + `AssemblyLoadContext`.~~ — заменено решением 26. Остаётся: вход — один reflection-вызов `MyTool.Application` внутри ALC; кнопки ставятся обёрткой `AddButton<T>` / `SetAvailability<T>` и указывают на однострочные прокси в Launcher (`: ProxyCommand` / `: ProxyAvailability`), наличие прокси проверяется при старте; `IsRepackable=false`. | 2026-10-24 |
| 26 | Изоляция — **всегда свой Launcher + `AssemblyLoadContext`** на всех версиях Revit (2025–2027), как в AnalyseTool. Один `.addin` — в `MyTool.Launcher`, без `ManifestSettings`; штатная изоляция Revit не используется. Отклонение от структуры Nice3point; убрать Launcher можно, когда штатная изоляция станет надёжной. | 2026-10-24 |
