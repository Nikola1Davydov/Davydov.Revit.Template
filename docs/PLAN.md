# Davydov.Revit.Template — план

Стартовый проект для новых Revit-плагинов. Берёт проверенные решения из AnalyseTool,
но организован как ASP.NET Core: host-builder, DI, конфигурация, middleware, «контроллеры»-команды.

> Статус: черновик, обсуждается. Решения фиксируются в разделе «Решения», выбор, который ещё не сделан, —
> в «Открытых вопросах» (буквы A, B, … — ссылки на них по тексту).

## 1. Идея: ASP.NET Core → Revit

| ASP.NET Core | В шаблоне |
| --- | --- |
| `WebApplication.CreateBuilder()` | `RevitApp.CreateBuilder(UIControlledApplication)` — настоящий Generic Host внутри или свой лёгкий builder (вопрос A) |
| `Program.cs` | `Program.cs` — единственное место композиции |
| `builder.Services` (DI) | то же, `Microsoft.Extensions.DependencyInjection` |
| `appsettings.json` + `IOptions<T>` | `appsettings.json` рядом с DLL + пользовательские настройки в `%AppData%` (раздел 11) |
| `ILogger<T>` | то же, Serilog как провайдер |
| Kestrel (`IServer`) | транспорты: WebView2, позже MCP (`ITransport`) |
| Middleware (`app.Use…`) | конвейер команд (раздел 5.4) |
| Controllers + `AddControllers()` / `MapControllers()` | классы команд + один вызов `builder.Services.AddCommands()` |
| Minimal API `app.MapGet(...)` | пока нет — только классы (решение 10) |
| Model binding | payload автоматически → `TRequest` |
| `IHostedService` | фоновые сервисы со стартом/остановкой в OnStartup/OnShutdown |
| `IHostApplicationLifetime` | `ApplicationStarted` = `ApplicationInitialized` Revit, `ApplicationStopping` = OnShutdown (детали — вопрос A) |
| SignalR (`IHubContext.Clients.All.SendAsync`) | `IClientEvents.PublishAsync` — push событий во все открытые окна |
| `wwwroot` / SPA ClientApp | `ClientApp/` (React + Vite), dev-сервер с HMR в Debug |
| `SynchronizationContext` | `IRevitDispatcher` — единственный путь в Revit API (ExternalEvent) |

### Как это выглядит

```csharp
// Program.cs
public static class Program
{
    public static RevitApp Build(UIControlledApplication revit)
    {
        RevitAppBuilder builder = RevitApp.CreateBuilder(revit);

        builder.Services.AddSingleton<IFamiliesService, FamiliesService>();
        builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
        builder.Services.AddCommands();          // сгенерирован source generator'ом: все команды сборки, сразу открыты транспортам
        builder.Services.AddRevitEvents();       // обработчики событий Revit, см. раздел 6
        builder.Services.AddWebView2Transport();

        RevitApp app = builder.Build();

        app.UseExceptionHandling();              // порядок и полный список middleware — раздел 5.4
        app.UseCommandLogging();
        app.ForwardToClient<ActiveDocumentChanged>();

        app.Ribbon("MyTool", tab => tab          // лента — раздел 8
            .Panel("Семейства")
                .Button("Семейства", window: "/families", document: DocumentBinding.Pinned)
                .Button("Очистить",  command: "PurgeFamilies"));
        return app;
    }
}
```

```csharp
// Commands/Families/RenameFamily.cs — «контроллер», имя команды = "RenameFamily"
[Command(Description = "Переименовать семейство")]
public sealed class RenameFamily(IRevitDispatcher revit, ILogger<RenameFamily> log)
    : ICommand<RenameFamilyRequest, RenameFamilyResult>
{
    public Task<RenameFamilyResult> ExecuteAsync(RenameFamilyRequest request, CommandContext context) =>
        revit.InvokeAsync(context.Document, doc => { /* транзакция */ });   // документ запроса, не «активный сейчас»
}
```

## 2. Что берём из AnalyseTool и что меняем

| В AnalyseTool | В шаблоне |
| --- | --- |
| статический `CoreServices` (service locator) | DI-контейнер |
| `Activator.CreateInstance` для команд | scope на запрос, внедрение через конструктор |
| `Payload.As<T>()` + `InputType` в атрибуте | `ICommand<TRequest,TResponse>` — схема берётся из `TRequest` |
| `CommandRequest.Gate` | middleware в конвейере |
| статический `Log` | `ILogger<T>` |
| `mcp.json` и свои файлы настроек | `IConfiguration` + `IOptions<T>` + `IUserSettings` для записи (раздел 11) |
| `XxxController.Initialize(...)` в bootstrap | `IHostedService` |
| `IProgressAware` (внедрение через свойство) | `context.Progress` |
| Launcher ищет `RibbonHost` по FQN-строкам и вызывает методы по именам-строкам | одна сгенерированная точка входа + пул слотов для кнопок (раздел 8) |
| по классу `IExternalCommand` в Launcher на каждую кнопку, написанному руками | пул одинаковых слотов, кнопки описываются в `Program.cs` (раздел 8) |
| статический `DocumentTracker`, второй `ExternalEvent` в `RibbonEventHub`, трюки с `Idling` в `DockPaneHost`, ручной `SendEvent` в док-панели | один механизм событий (раздел 6) |
| команды берут `ActiveUIDocument` в момент вызова | документ — часть запроса, `context.Document` (раздел 7) |
| создание окружения WebView2 (`UserDataFolder`) повторено в 5 окнах; нет защиты навигации | одно место в Hosting.Wpf + allowlist навигации (раздел 9) |

Оставляем без изменений (проверено в бою):
- Launcher + изолированный `AssemblyLoadContext` — без него Microsoft.Extensions.* конфликтуют с другими плагинами.
- Один долгоживущий `ExternalEvent` с очередью (`RevitTaskHub`).
- Конфигурации `Debug|Release R25/R26/R27`, TFM из props, Central Package Management.
- Протокол WebView2: Request / Response / Progress / Event с `id`; терпимость к окну, закрытому посреди команды.
- Правило «headless»: Hosting без WPF, проверка границ в CI (`Check-Boundaries.ps1`, раздел 3.1).
- NUKE: сборка клиента → плагин → инсталлятор → релиз.
- Debug-сборка сама деплоится в `%AppData%\Autodesk\Revit\Addins\<year>\`.

Аналог для сравнения: Nice3point.Revit.Templates / Revit.Toolkit (там тоже есть Host + DI) — посмотреть перед фазой 2.

## 3. Структура решения (черновик)

```
Davydov.Revit.Template/
├─ src/
│  ├─ Davydov.Revit.Hosting/              «фреймворк» (аналог Microsoft.AspNetCore.*), headless
│  │  ├─ Builder/        RevitAppBuilder, RevitApp
│  │  ├─ Commands/       ICommand<…>, [Command], CommandContext, реестр, model binding, встроенные Host.*-команды
│  │  ├─ Pipeline/       ICommandMiddleware + встроенные middleware
│  │  ├─ Events/         RevitEventSource, IAppEventHandler<>, IRevitEventHandler<>, RevitUpdater, IClientEvents
│  │  ├─ Documents/      IDocumentRegistry, DocumentKey, DocumentRef, middleware DocumentBinding
│  │  ├─ Threading/      IRevitDispatcher (ExternalEvent)
│  │  ├─ Ribbon/         описание ленты, таблица слотов (без WPF — только данные)
│  │  ├─ Settings/       IUserSettings
│  │  └─ Transports/     ITransport, IUserNotifier (абстракция)
│  ├─ Davydov.Revit.Hosting.Generators/   source generator + анализаторы (netstandard2.0), раздел 5.3
│  ├─ Davydov.Revit.Hosting.Wpf/          WebView2: окружение, окна, док-панель, транспорт; построение ленты; IUserNotifier
│  ├─ Davydov.Revit.Template/              само приложение (аналог проекта WebApplication)
│  │  ├─ Program.cs
│  │  ├─ appsettings.json
│  │  ├─ Commands/       «контроллеры», по фичам: Commands/Families/…
│  │  ├─ EventHandlers/  обработчики событий и апдейтеры, по фичам
│  │  ├─ Services/
│  │  ├─ Models/
│  │  └─ ClientApp/      React + Vite (раздел 10)
│  └─ Davydov.Revit.Template.Launcher/    тонкий IExternalApplication + ALC + пул слотов
├─ tests/                Hosting.Tests (без Revit), Generators.Tests (снимки + диагностики), ClientApp — Vitest
├─ build/                NUKE
├─ CLAUDE.md             инструкции для ИИ-ассистентов (фаза 7 — копия попадает в каждый новый проект)
├─ Directory.Build.props, Directory.Packages.props
└─ .template.config/     template.json (для dotnet new)
```

### 3.1. Зависимости проектов (проверяет `Check-Boundaries.ps1` в CI)

| Проект | Ссылается на | Правила |
| --- | --- | --- |
| `Hosting` | Revit API (только компиляция), Microsoft.Extensions.* | **headless**: без `UseWPF`, `System.Windows`, `MessageBox`. Наши проекты — ни одного |
| `Hosting.Generators` | только `Microsoft.CodeAnalysis.*` (netstandard2.0) | не ссылается на Hosting: работает по именам типов из метаданных |
| `Hosting.Wpf` | Hosting, WebView2 | единственное место WPF и диалогов |
| приложение | Hosting, Hosting.Wpf, Generators (как анализатор) | в `Commands/`, `Services/`, `EventHandlers/` — без `System.Windows` (UI — только через `IUserNotifier` и фронтенд) |
| `Launcher` | приложение — **только** ради порядка сборки и `deps.json` | в коде Launcher нельзя использовать типы приложения/Hosting: они загрузились бы в default ALC в обход изоляции |
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
// Команда без результата: ICommand<SelectElementsRequest, Empty>. Свести ли всё к одному интерфейсу — вопрос B.

public sealed class CommandContext
{
    public string CommandName { get; }
    public string Source { get; }                         // "webview2", "ribbon", позже "mcp"
    public CommandMetadata Command { get; }                // метаданные из [Command]
    public DocumentRef Document { get; }                   // привязанный документ (раздел 7)
    public IProgress<ProgressInfo> Progress { get; }
    public CancellationToken CancellationToken { get; }    // отменяется и при закрытии документа
    public JsonElement RawPayload { get; }                 // для middleware; команде приходит уже TRequest
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandAttribute(string? name = null) : Attribute
{
    public string? Description { get; set; }
    public bool ReadOnly { get; set; }
    public bool Destructive { get; set; }
    public bool RequiresActiveDocument { get; set; }       // нужен UIDocument: выделение, виды
    public DocumentKind DocumentKind { get; set; }         // Any / Project / Family
}

// Доступ к Revit
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

// Уведомление пользователя (реализация — Hosting.Wpf: TaskDialog / toast во фронтенде)
public interface IUserNotifier { void Error(string title, string message); void Info(string title, string message); }
```

## 5. Команды

### 5.1. Сериализация — System.Text.Json

- Везде STJ: протокол WebView2, model binding команд, `appsettings.json`, ответы. Newtonsoft не используется
  (в AnalyseTool он был в публичном контракте — `JToken`, `RevitPayload`).
- Одни общие `JsonSerializerOptions` в DI, настраиваются как в ASP.NET Core:
  `builder.Services.ConfigureJson(o => …)`. По умолчанию: camelCase, регистронезависимое чтение, enum как строки.
- Payload приходит как `JsonElement` и десериализуется в `TRequest` один раз, в конце конвейера (model binding).
- В Hosting — конвертеры для типов Revit, которые встречаются в DTO: `ElementId` ↔ `long` и т.п.
- **Версии.** System.Text.Json — встроенная в runtime, NuGet-пакет System.Text.Json не подключаем. Для этого
  **версии Microsoft.Extensions.\* совпадают с runtime каждого TFM**: 8.x для R25/R26 (net8), 10.x для R27 (net10).
  Пакет новее runtime (например, Microsoft.Extensions.Hosting 9.x на net8) сам притянет NuGet-версию
  System.Text.Json. В CPM это делается условием по `TargetFramework`.
  Зачем: одна копия STJ на процесс плагина, предсказуемые версии, меньше файлов в поставке. (В изолированном ALC
  вторая копия формально допустима — типы STJ мы с Revit не делим, — но пользы от неё нет.)
- Следствие: `JsonSchemaExporter` (нужен для MCP-схем) есть только с .NET 9, поэтому на R25/26 вопрос схем
  решаем в фазе 8.
- Newtonsoft допустим только если его требует сторонняя библиотека, и только внутри её обёртки.

### 5.2. Имена команд

- По умолчанию имя команды = имя класса (`RenameFamily` → `"RenameFamily"`), атрибут `[Command]` не обязателен.
- `[Command("Name")]` — только чтобы переопределить имя (например, сохранить старое имя после переименования класса).
- Суффикс `Command` отбрасывается, как `Controller` в ASP.NET Core: `RenameFamilyCommand` → `"RenameFamily"`.
- Сравнение без учёта регистра.
- Встроенные команды Hosting имеют префикс `Host.` (`Host.GetOpenDocuments`, `Host.PickFolder`, `Host.OpenExternalLink`).
  Точка в имени допустима **только** у встроенных команд, поэтому с командами приложения они не пересекаются.
- Конфликт имён внутри сборки — **ошибка компиляции** (`DRT001`); между сборками (приложение + Hosting) —
  **ошибка при `Build()`** с указанием обоих классов. Не тихий пропуск с записью в лог, как в AnalyseTool.
- Защита от переименований — генерация TS-типов из команд (фаза 7): переименовал класс → фронтенд не скомпилируется.

### 5.3. Регистрация — source generator, без reflection

Команды и обработчики событий находит **source generator** при компиляции (в VS — прямо при наборе кода).
Reflection-сканирования нет вообще.

- Проект `Davydov.Revit.Hosting.Generators` (netstandard2.0, `IIncrementalGenerator`), подключается к приложению
  (и к Hosting / Hosting.Wpf — для их встроенных команд) как анализатор:
  `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
  Версия `Microsoft.CodeAnalysis.CSharp` — минимальная из поддерживаемых VS/SDK, закрепляется в CPM.
- Генератор находит не абстрактные классы, реализующие `ICommand<TResponse>` / `ICommand<TRequest, TResponse>`,
  `IAppEventHandler<T>`, `IRevitEventHandler<TArgs>`, и применяет правила имён (раздел 5.2). **Правила имён живут
  только в генераторе.**
- Генерирует в сборку приложения:
  - методы `AddCommands()` и `AddRevitEvents()` — Hosting их не объявляет. Не подключил генератор → `Program.cs`
    не компилируется, а не «плагин загрузился, команд нет»;
  - **точку входа** `Davydov.Revit.Generated.AppEntry` — по найденному `Program.Build(UIControlledApplication)`.
    Launcher ищет только это одно имя (раздел 8). Для Hosting/Hosting.Wpf генерируется внутренний
    `AddHostingCommands()`, который builder вызывает сам.

  ```csharp
  // <auto-generated/> obj/…/CommandRegistration.g.cs
  namespace MyTool;

  internal static class GeneratedCommandRegistration
  {
      public static IServiceCollection AddCommands(this IServiceCollection services)
      {
          services.AddScoped<global::MyTool.Commands.Families.RenameFamily>();
          services.AddCommandDescriptor(new CommandDescriptor(
              Name: "RenameFamily",
              CommandType: typeof(global::MyTool.Commands.Families.RenameFamily),
              RequestType: typeof(global::MyTool.Commands.Families.RenameFamilyRequest),
              ResponseType: typeof(global::MyTool.Commands.Families.RenameFamilyResult),
              Metadata: new CommandMetadata(Description: "Переименовывает семейство"),
              Invoke: static async (sp, payload, context, json) =>      // прямой вызов, без reflection
              {
                  var command = sp.GetRequiredService<global::MyTool.Commands.Families.RenameFamily>();
                  var request = payload.Deserialize<global::MyTool.Commands.Families.RenameFamilyRequest>(json)!;
                  return await command.ExecuteAsync(request, context);
              }));
          // … остальные команды
          return services;
      }
  }
  ```

- Диагностики (красное подчёркивание в редакторе, ошибка сборки):

  | Код | Что ловит |
  | --- | --- |
  | `DRT001` | два класса дают одно имя команды — указывает оба |
  | `DRT002` | `TRequest` — интерфейс/абстрактный класс, STJ не сможет создать |
  | `DRT003` | класс реализует несколько `ICommand<…>` сразу |
  | `DRT004` | нет `Program.Build(UIControlledApplication)` или у него другая сигнатура — точку входа не сгенерировать |
  | `DRT005` | точка в имени команды приложения (зарезервирована за `Host.*`) |
  | `DRT010` | `UIApplication.ActiveUIDocument` где угодно в сборке приложения — использовать `context.Document` (раздел 7). Исключение — явный `[AllowActiveDocument("причина")]` на классе или методе |

- Команды только для отладки — обычный `#if DEBUG` вокруг класса: генератор видит символы препроцессора, в Release
  команды просто нет.
- Что генератор проверить не может — зарегистрированы ли в DI зависимости из конструкторов и конфликты имён между
  сборками. Это проверяет `Build()` (`ValidateOnBuild` + проверка реестра).
- Тесты генератора — снимки сгенерированного кода (Verify.SourceGenerators) + тест на каждую диагностику.
- Сгенерированный код виден в VS: Dependencies → Analyzers; в `obj/` — с `EmitCompilerGeneratedFiles=true` в Debug.

### 5.4. Конвейер middleware

Встроенные слои и их порядок:

| # | Middleware | Что делает | Подключение |
| --- | --- | --- | --- |
| 1 | `ExceptionHandling` | любое исключение → ответ `{ error }`; если у вызова нет окна-получателя (`Source = "ribbon"`) — `IUserNotifier.Error` | `app.UseExceptionHandling()`, всегда первым |
| 2 | `DocumentBinding` | находит документ запроса, проверяет `RequiresActiveDocument` / `DocumentKind`, кладёт в `context.Document`, отменяет токен при закрытии документа (раздел 7) | неявно или `app.UseDocumentBinding()` — вопрос C |
| 3 | `CommandLogging` | кто, что, откуда, сколько длилось, чем кончилось | `app.UseCommandLogging()` |
| 4 | `ConfirmDestructive` | для `Destructive = true` спрашивает пользователя (`IUserPrompt`, реализация в Hosting.Wpf) | для каких источников — вопрос D |
| 5 | `SingleFlight` | не даёт запустить ту же команду второй раз, пока первая не закончилась (двойной клик) | `app.UseSingleFlight()`, по желанию |
| — | свои слои | `app.UseMiddleware<T>()` | между встроенными и командой |
| — | конечная точка | model binding (`JsonElement` → `TRequest`) + вызов команды | всегда последней |

Middleware не содержит бизнес-логики и не ходит в модель: выполняется вне потока Revit, видит только `RawPayload`
и метаданные.

### 5.5. Потоки, прогресс и долгие команды

- **Транспорт сразу уводит вызов из UI-потока.** `WebMessageReceived` приходит в UI-потоке Revit (он же поток
  Revit API). Транспорт запускает конвейер в thread pool (`Task.Run`), а ответы и прогресс отправляет обратно через
  `Dispatcher` окна. Код команд и middleware **никогда** не выполняется в UI-потоке — тяжёлые вычисления между
  `await` не подвешивают Revit.
- **`InvokeAsync` выполняется синхронно в потоке Revit**, а это и UI-поток. Пока тело работает, окно не получает
  ни прогресс, ни что-либо ещё: `PostWebMessageAsJson` ждёт освобождения потока. Для одного долгого `InvokeAsync`
  прогресс бесполезен.
- **Долгая работа — порциями:** `revit.InvokeBatchedAsync(context.Document, items, batchSize: 200, (doc, batch) => …,
  context.Progress, context.CancellationToken)`. Между порциями Revit получает управление: окно показывает прогресс,
  пользователь может отменить.
- **Цена порций при записи:** транзакцию и `TransactionGroup` нужно закрыть до выхода из обработчика external event,
  поэтому запись порциями даёт **несколько шагов отмены**. Команда выбирает: одна транзакция (один шаг отмены, без
  прогресса) или порции (прогресс и отмена, несколько шагов). Для чтения порции бесплатны.

## 6. События Revit

Один механизм вместо четырёх из AnalyseTool. Три вида реакций:

| Что нужно | Механизм | Где выполняется |
| --- | --- | --- |
| Отреагировать в C#: обновить кэш, записать в лог, уведомить | **события приложения** `IAppEventHandler<T>` | асинхронно, вне потока Revit; в модель — через `IRevitDispatcher` |
| Вмешаться в само событие Revit: убрать предупреждение, ответить на диалог, отменить | **синхронные обработчики** `IRevitEventHandler<TArgs>` | синхронно, внутри события Revit; должны быть быстрыми |
| Изменить модель в ответ на её изменение | **`RevitUpdater`** (обёртка над `IUpdater`) | в той же транзакции; в `DocumentChanged` Revit менять модель запрещает |

Плюс пересылка во фронтенд (`ForwardToClient`, `IClientEvents`) и жизненный цикл (`IHostApplicationLifetime`).

### События приложения

Hosting сам подписывается на события Revit и отдаёт обработчикам неизменяемые записи — снимок данных,
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
  на `DocumentClosing` запоминает `DocumentId → DocumentKey`, а на `DocumentClosed` выпускает событие с ключом —
  привязанное окно понимает, что закрыли именно его документ.
- `FromOurCommand`: `IRevitDispatcher` помечает, что сейчас выполняется работа нашей команды; `DocumentChanged`,
  пришедший в это время, получает `true`.

### Регистрация

```csharp
builder.Services.AddRevitEvents();                 // сгенерирован: все IAppEventHandler<> / IRevitEventHandler<> сборки
builder.Services.Configure<RevitEventOptions>(o =>
{
    o.Debounce<SelectionChanged>(TimeSpan.FromMilliseconds(150));
    o.Debounce<ModelChanged>(TimeSpan.FromMilliseconds(300));
});
builder.Services.AddUpdater<SheetNumberUpdater>(); // регистрация в OnStartup, снятие в OnShutdown

RevitApp app = builder.Build();

app.ForwardToClient<ActiveDocumentChanged>();       // → React: useRevitEvent("ActiveDocumentChanged")
app.ForwardToClient<SelectionChanged>();
app.ForwardToClient<ModelChanged>();
app.ForwardToClient<DocumentClosed>();
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

// Апдейтер: UpdaterId, регистрацию и триггеры берёт на себя Hosting
public sealed class SheetNumberUpdater : RevitUpdater
{
    protected override void ConfigureTriggers(UpdaterTriggers t) =>
        t.OnAdd<ViewSheet>().OnParameterChange<ViewSheet>(BuiltInParameter.SHEET_NUMBER);

    protected override void Execute(UpdaterData data) { /* правка модели в той же транзакции */ }
}
```

### Жизненный цикл

`IHostApplicationLifetime.ApplicationStarted` срабатывает на `ApplicationInitialized` (UIApplication уже есть,
можно идти в модель), `ApplicationStopping` — в OnShutdown. Как именно это связать, зависит от вопроса A.
`Idling` наружу не отдаётся: им пользуется только Hosting (диспетчер, готовность док-панели).

### Как устроено внутри

```
Revit-событие (поток Revit)
  └─ RevitEventSource (Hosting, подписан в OnStartup через UIControlledApplication)
       ├─ синхронные IRevitEventHandler<TArgs> → сразу, внутри события
       └─ снимок данных → Channel → фоновая обработка
              ├─ debounce / склейка (SelectionChanged, ModelChanged)
              ├─ IAppEventHandler<T> — каждый в своём DI-scope
              └─ ForwardToClient → IClientEvents → все открытые окна и док-панели
```

Правила:
- Подписка только в OnStartup — в обработчике WPF `Loaded` Revit бросает исключение (урок `DockPaneHost`).
- На «горячие» `DocumentChanged` и `SelectionChanged` подписываемся, только если есть хоть один обработчик или пересылка.
- Ни одно исключение из обработчика не уходит в Revit: ловится и логируется; ошибка одного обработчика не мешает остальным.
- В OnShutdown всё отписывается, апдейтеры снимаются.

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
3. **Middleware `DocumentBinding`** (раздел 5.4): находит документ по ключу (закрыт → ошибка `DocumentClosed`),
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
   ```

   Изменять модель можно и в неактивном документе (Revit API это позволяет, и пользователь смотрел именно на данные A).
   Действия с интерфейсом — только в активном, иначе отказ.
6. **Защита при компиляции:** анализатор `DRT010` (раздел 5.3) запрещает `UIApplication.ActiveUIDocument` во всей
   сборке приложения, в том числе внутри `InvokeAsync(Func<UIApplication, T>)` — эта перегрузка для действий уровня
   приложения, а не для обхода привязки. Исключение — только явный `[AllowActiveDocument("причина")]`.

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
- На ленте: `.Button("Семейства", window: "/families", document: DocumentBinding.Pinned)`.

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

## 8. Лента и вход из Revit

**Ограничение Revit:** кнопка ленты указывает на класс `IExternalCommand` по имени типа и пути к DLL, и Revit
создаёт его сам — в default ALC. Наш код живёт в изолированном ALC, Revit его там не видит. Поэтому классы для кнопок
обязаны лежать в Launcher и перенаправлять вызов внутрь ALC (так и сделано в AnalyseTool, но каждой кнопке там
отвечает свой рукописный класс, а внутрь вызов уходит по имени метода-строке).

### Вход

```
Revit → Launcher.App.OnStartup
  1. создаёт изолированный ALC, загружает сборку приложения
  2. reflection — ОДИН раз, по одному имени: Davydov.Revit.Generated.AppEntry.Start(...)
     (тип генерирует генератор, DRT004 гарантирует, что он есть; нет — TaskDialog, а не пустая лента)
  3. Start вызывает Program.Build, запускает host, строит ленту и возвращает таблицу делегатов
     (Func<int, UIApplication, Result> для слотов и т.п.)
  4. дальше Launcher вызывает только эти делегаты — без строк и reflection
```

Делегаты свободно пересекают границу ALC: в их сигнатурах только типы .NET и Revit API, а они общие.

### Кнопки — пул слотов

- В Launcher — фиксированный пул одинаковых классов `Slot00 … Slot63 : IExternalCommand`. Каждый просто вызывает
  делегат со своим номером: `Slot07.Execute → invokeSlot(7, uiApp)`.
- `app.Ribbon(...)` в `Program.cs` описывает вкладки, панели и кнопки; Hosting раздаёт кнопкам слоты по порядку.
  Кнопок больше, чем слотов, → ошибка при `Build()` с понятным текстом (пул расширяется одной строкой в Launcher).
- Доступность кнопок — небольшой фиксированный набор `IExternalCommandAvailability` в Launcher: `Always`,
  `RequiresDocument`, `RequiresProject`; кнопка выбирает его в описании (`.RequiresProject()`).
- Кнопка делает одно из двух: открывает окно (`window: "/families"` + режим follow/pinned) или вызывает команду
  (`command: "PurgeFamilies"`, `Source = "ribbon"`, ошибки — через `IUserNotifier`).
- В Debug — панель «Dev» с кнопками для проверки команд до появления UI (фазы 3–4, раздел 12).
- Слоты против описания кнопок атрибутами, чтобы генератор создавал ровно нужные классы, — вопрос E.

## 9. WebView2: окружение и безопасность

- **Одно окружение на плагин** (`WebViewEnvironmentProvider` в Hosting.Wpf): проверка наличия runtime
  (`GetAvailableBrowserVersionString`; нет — понятное сообщение со ссылкой на установку), `UserDataFolder` в
  `%LOCALAPPDATA%\<Tool>\WebView2` (папка Revit недоступна для записи). В AnalyseTool это повторено в пяти окнах.
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

Два слоя, чтобы фреймворк можно было сменить без изменений в C#:

1. **`revit-bridge`** — чистый TypeScript, без зависимостей от React. Переносится из `RevitBridge.ts`
   AnalyseTool: `invoke(command, payload, { onProgress, signal })`, подписка на события (`on("ActiveDocumentChanged", …)`),
   сопоставление Request/Response по `id`. В Debug без Revit (в обычном браузере) — mock-режим.
2. **React-приложение** поверх моста.

Стек:

| Задача | Выбор |
| --- | --- |
| Сборка | Vite + TypeScript |
| Компоненты | shadcn/ui (Radix) + Tailwind 4 — код компонентов копируется в `src/components/ui/` и принадлежит проекту |
| Таблицы на тысячи элементов | своя `<DataTable>` в шаблоне (`src/components/data-table/`): TanStack Table + TanStack Virtual + разметка shadcn; страницы описывают только колонки |
| Деревья (категории → семейства → типы) | в shadcn нет — своя `<TreeView>` в шаблоне или отдельная библиотека (вопрос F) |
| Вызовы команд Revit | TanStack Query поверх `revit-bridge` (кэш, повторы, состояние загрузки) |
| Состояние UI | Zustand |
| Роутинг | React Router (hash/memory — страница открывается не с веб-сервера) |
| Графики / 3D | Recharts, three.js (через react-three-fiber при необходимости) |
| Тесты | Vitest для `revit-bridge` и хуков (mock-режим моста) |

Пример вызова команды и подписки на события:

```tsx
const families = useCommand<FamilyDto[]>("GetFamilies", {
  refetchOn: ["ActiveDocumentChanged", "ModelChanged"],     // перезапросить при смене документа / правке модели
});
const rename = useCommandMutation("RenameFamily");

useRevitEvent<SelectionChanged>("SelectionChanged", e => setSelectedIds(e.elementIds));

if (families.isLoading) return <Spinner />;
return <FamiliesTable data={families.data} onRename={r => rename.mutate(r)} />;
```

Позже (фаза 7): типы `TRequest`/`TResponse` и событий для TypeScript генерировать из C#, чтобы фронтенд и бэкенд не расходились.

## 11. Настройки и логи

- **Слои конфигурации** (как в ASP.NET Core, позднее перекрывает раннее):
  1. `appsettings.json` рядом с DLL — значения по умолчанию, только чтение;
  2. `%AppData%\<Tool>\<RevitVersion>\usersettings.json` — то, что пользователь поменял в интерфейсе;
  3. переменные окружения `MYTOOL_…` — для отладки.
- **Чтение** — обычные `IOptions<T>` / `IOptionsMonitor<T>` (`reloadOnChange` для пользовательского файла).
- **Запись** — `IUserSettings`: `await settings.UpdateAsync<AiOptions>("Ai", o => o.Model = "…")` пишет секцию в
  `usersettings.json`, `IOptionsMonitor` подхватывает изменение. Так переносятся `mcp.json` и настройки
  AI-провайдеров из AnalyseTool. Секреты (API-ключи) — не в JSON, а через DPAPI (`ProtectedData`, текущий
  пользователь).
- **Логи** — Serilog, файл `%LOCALAPPDATA%\<Tool>\logs\<RevitVersion>\log-.txt` с ротацией по дням; уровень и sinks —
  секция `Serilog` в `appsettings.json`. Каждая команда логируется с `CommandName`, `Source`, `DocumentKey`
  (через `CommandLogging`).

## 12. Тесты и цикл разработки

- **Unit-тесты без Revit** (`tests/Hosting.Tests`): конвейер, middleware, model binding, логика `DocumentBinding`,
  реестр команд — с фейковыми `IRevitDispatcher` / `IDocumentRegistry`. Типы Revit (`Document`, `ElementId`, …) вне
  Revit создать нельзя, поэтому логика, которую хотим тестировать, не трогает их напрямую.
- **Генератор** — снимки и диагностики (раздел 5.3).
- **Фронтенд** — Vitest (раздел 10).
- **Внутри Revit:** библиотека для интеграционных тестов (например, ricaun.RevitTest) или ручной чек-лист —
  вопрос G.
- **До появления UI (фазы 3–4)** — Debug-панель «Dev» на ленте: кнопки, вызывающие команды; результат — в лог и
  `TaskDialog`.
- **Цикл разработки C#.** У фронтенда есть HMR, у C# — только перезапуск Revit. В фазе 1 проверить .NET Hot Reload /
  Edit and Continue при подключённом к Revit отладчике и изолированном ALC; результат записать в CLAUDE.md
  шаблона.

## 13. Фазы

0. **Решения** — открытые вопросы закрываются до фазы, которой они нужны: E (лента) — до фазы 1, A — до фазы 2,
   B, C, D, G — до фазы 3, F — до фазы 5.
1. **Скелет** — решение, props, конфигурации R25–R27, CPM (версии Microsoft.Extensions.* по TFM), Launcher + ALC,
   проект генератора с точкой входа `AppEntry` и `DRT004`, пул слотов, автодеплой, базовый Serilog,
   проверка Hot Reload.
   Готово, когда: в Revit появляется кнопка на ленте (через слот) и пишет в лог.
2. **Hosting** — builder (вопрос A), DI, конфигурация и `IUserSettings`, логирование, `IRevitDispatcher`
   (все перегрузки, включая `InvokeBatchedAsync`), жизненный цикл hosted-сервисов, `IUserNotifier`, описание ленты.
   Готово, когда: сервис из DI читает документ через `IRevitDispatcher`; настройка, записанная через
   `IUserSettings`, переживает перезапуск Revit.
3. **Команды** — генератор (`AddCommands`, `DRT001–005`, `DRT010`), проверки `Build()` (DI, конфликты между
   сборками), встроенные `Host.*`-команды, конвейер с встроенными middleware (раздел 5.4), запуск конвейера в thread
   pool, `IDocumentRegistry`, `DocumentBinding`, `context.Document`, Debug-панель «Dev».
   Готово, когда: команда с DI и `TRequest` вызывается с ленты и проходит через middleware; команда с документом A
   выполняется в A при активном B; ошибка команды с ленты показывается через `IUserNotifier`; есть unit-тесты на
   конвейер.
4. **События** — `RevitEventSource`, события приложения (включая `DocumentClosed` с ключом), синхронные обработчики
   (регистрирует тот же генератор — `AddRevitEvents`), `RevitUpdater`, debounce, `IHostApplicationLifetime`.
   Готово, когда: смена документа и правка модели доходят до `IAppEventHandler`, апдейтер срабатывает,
   исключение в обработчике не роняет Revit.
5. **UI** — окружение WebView2 и защита навигации (раздел 9), окно и док-панель, транспорт, `revit-bridge` (TS),
   React ClientApp, Vite HMR в Debug, `ForwardToClient` / `useRevitEvent` / `refetchOn`, `RevitDocumentProvider`
   (follow/pinned), shadcn/ui + своя `<DataTable>` (сортировка, фильтры, выделение, виртуальный скролл).
   Готово, когда: кнопка в React вызывает команду и показывает результат и прогресс (на `InvokeBatchedAsync`);
   при смене документа док-панель сама обновляется, а привязанное окно остаётся на своём документе; переход по
   внешней ссылке открывает системный браузер.
6. **Сборка и выпуск** — NUKE, инсталлятор, CI: проверка границ по таблице 3.1, сборка R25 (net8) и R27 (net10),
   тесты Hosting и генератора, сборка и lint фронтенда.
7. **Шаблон** — `.template.config`, `dotnet new davydov-revit -n MyTool`, параметры; CLAUDE.md для новых проектов
   (правила документов, команд, событий, потоков); генерация TS-типов (отдельная утилита после сборки через
   `MetadataLoadContext` — source generator не может писать файлы в `ClientApp/`).
8. **Опционально** — MCP, Roslyn-скрипты (переносятся из AnalyseTool как модули `AddMcp()` и т.п.).
   Системы сторонних расширений нет (решение 12).

## 14. Открытые вопросы

- **A. Host-builder.** (а) Настоящий Generic Host — `HostApplicationBuilder` внутри `RevitAppBuilder`: конфигурация,
  логирование, DI, `IHostedService` и lifetime бесплатно; минусы — больше зависимостей, `host.Start()` синхронно в
  `OnStartup`, связь `ApplicationStarted` с `ApplicationInitialized` нужно делать своим `IHostLifetime`.
  (б) Свой лёгкий builder на `ServiceCollection` + `ConfigurationBuilder`: меньше зависимостей, но hosted-сервисы и
  lifetime пишем сами. Рекомендация — (а).
- **B. Команды без входа / без результата.** (а) Как сейчас: `ICommand<TResponse>` + `ICommand<TRequest, TResponse>`,
  «нет результата» — `Empty`. (б) Один интерфейс `ICommand<TRequest, TResponse>`, «нет входа» — тоже `Empty`:
  ровно один способ, генератору проще, но `ICommand<Empty, …>` многословнее. Рекомендация — (б).
- **C. `DocumentBinding` неявно или явно.** (а) Всегда включён, builder ставит его сам сразу после
  `ExceptionHandling` — забыть нельзя. (б) Явный `app.UseDocumentBinding()`, как `UseRouting` в ASP.NET Core.
  Рекомендация — (а).
- **D. `ConfirmDestructive` — для каких источников.** (а) Только для MCP и будущих удалённых транспортов: в UI
  подтверждение и так делает сам интерфейс. (б) Для всех источников, включая WebView2 и ленту. Рекомендация — (а);
  до фазы 8 (MCP) слой тогда фактически не нужен.
- **E. Кнопки ленты.** (а) Пул слотов в Launcher, кнопки описываются в `Program.cs` (раздел 8). (б) Кнопки
  описываются атрибутами на командах/окнах, генератор создаёт ровно нужные классы-слоты в Launcher — без пула, но
  ленту нельзя собрать динамически. Рекомендация — (а).
- **F. Деревья во фронтенде:** своя `<TreeView>` или сторонняя библиотека? (фаза 5)
- **G. Тесты внутри Revit:** библиотека интеграционных тестов (например, ricaun.RevitTest — тесты NUnit запускаются
  в живом Revit) или только ручной чек-лист + Debug-панель «Dev»?

## 15. Решения

| # | Решение | Дата |
| --- | --- | --- |
| 1 | Hosting сначала живёт в исходниках шаблона; в NuGet выносим, когда API устоится. | 2026-10-24 |
| 2 | В базе только WebView2 + веб-фронтенд. MCP, Roslyn — позже, опциональными модулями (фаза 8). ~~Расширения/SDK~~ — отменено решением 12. | 2026-10-24 |
| 3 | Раздача через `dotnet new` (`.template.config`, переименование по `sourceName`). | 2026-10-24 |
| 4 | Revit 2025–2027: net8 для 2025/2026, net10 для 2027. | 2026-10-24 |
| 5 | Фронтенд — React (вместо Vue из AnalyseTool). Мост к Revit — отдельный TS-модуль без привязки к фреймворку. | 2026-10-24 |
| 6 | Сериализация — System.Text.Json везде, версия из runtime (без NuGet-пакета); поэтому версии Microsoft.Extensions.* совпадают с runtime каждого TFM. Newtonsoft — только внутри обёрток сторонних библиотек. | 2026-10-24 |
| 7 | Имя команды по умолчанию = имя класса; `[Command("…")]` — необязательное переопределение. | 2026-10-24 |
| 8 | События Revit — один механизм: события приложения (async), синхронные обработчики, `RevitUpdater`, пересылка во фронтенд. | 2026-10-24 |
| 9 | Документ — часть запроса: `DocumentKey` из реестра, middleware `DocumentBinding`, `context.Document` вместо `ActiveUIDocument`; окна follow/pinned. | 2026-10-24 |
| 10 | Один способ: команды и обработчики событий — только классы. Регистрация — один вызов `AddCommands()` (без `MapCommands`), проверки при `Build()`. Делегаты в стиле Minimal API (`MapCommand`, `app.On<T>`) — не делаем, пока не понадобятся. | 2026-10-24 |
| 11 | Компоненты — shadcn/ui + Tailwind 4 (не PrimeReact). Таблица — своя `<DataTable>` на TanStack Table + Virtual, собирается один раз в шаблоне. | 2026-10-24 |
| 12 | Системы сторонних расширений (загрузка чужих DLL, SDK-пакет, collectible ALC) нет — ни в базе, ни опционально. Hosting остаётся одним проектом, без отдельного Abstractions. | 2026-10-24 |
| 13 | Регистрация команд и обработчиков событий — source generator с фазы 3, без reflection. Генератор сам пишет `AddCommands()`/`AddRevitEvents()`, ловит ошибки при компиляции (`DRT001–005`), анализатор `DRT010` запрещает `ActiveUIDocument`. DI проверяется при `Build()`. | 2026-10-24 |
