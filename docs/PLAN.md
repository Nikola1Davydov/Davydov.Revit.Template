# Davydov.Revit.Template — план

Стартовый проект для новых Revit-плагинов. Берёт проверенные решения из AnalyseTool,
но организован как ASP.NET Core: Generic Host, DI, конфигурация, middleware, «контроллеры»-команды.

> Статус: черновик, обсуждается. Решения фиксируются в разделе «Решения».

## 1. Идея: ASP.NET Core → Revit

| ASP.NET Core | В шаблоне |
| --- | --- |
| `WebApplication.CreateBuilder()` | `RevitApp.CreateBuilder(UIControlledApplication)` |
| `Program.cs` | `Program.cs` — единственное место композиции |
| `builder.Services` (DI) | то же, `Microsoft.Extensions.DependencyInjection` |
| `appsettings.json` + `IOptions<T>` | `appsettings.json` рядом с DLL + пользовательский override в `%AppData%` |
| `ILogger<T>` | то же, Serilog как провайдер |
| Kestrel (`IServer`) | транспорты: WebView2, позже MCP (`ITransport`) |
| Middleware (`app.Use…`) | конвейер команд: логирование, ошибки, gate/подтверждение, … |
| Controllers + `AddControllers()` / `MapControllers()` | классы команд + один вызов `builder.Services.AddCommands()` |
| Minimal API `app.MapGet(...)` | пока нет — только классы (решение 10) |
| Model binding | payload автоматически → `TRequest` |
| `IHostedService` | фоновые сервисы со стартом/остановкой в OnStartup/OnShutdown |
| `IHostApplicationLifetime` | `ApplicationStarted` = `ApplicationInitialized` Revit, `ApplicationStopping` = OnShutdown |
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
        builder.Services.AddRevitEvents();       // обработчики событий Revit, см. раздел 5
        builder.Services.AddWebView2Transport();

        RevitApp app = builder.Build();

        app.UseExceptionHandling();
        app.UseCommandLogging();
        app.ForwardToClient<ActiveDocumentChanged>();

        app.Ribbon(tab => tab.Panel("Main").Button<ShowMainWindow>("Open"));
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
| `mcp.json` и свои файлы настроек | `IConfiguration` + `IOptions<T>` |
| `XxxController.Initialize(...)` в bootstrap | `IHostedService` |
| `IProgressAware` (внедрение через свойство) | `context.Progress` |
| Launcher ищет `RibbonHost` по FQN-строкам | Launcher вызывает одну известную точку входа (`Program.Build`) |
| статический `DocumentTracker`, второй `ExternalEvent` в `RibbonEventHub`, трюки с `Idling` в `DockPaneHost`, ручной `SendEvent` в док-панели | один механизм событий (раздел 5) |
| команды берут `ActiveUIDocument` в момент вызова | документ — часть запроса, `context.Document` (раздел 6) |

Оставляем без изменений (проверено в бою):
- Launcher + изолированный `AssemblyLoadContext` — без него Microsoft.Extensions.* конфликтуют с другими плагинами.
- Один долгоживущий `ExternalEvent` с очередью (`RevitTaskHub`).
- Конфигурации `Debug|Release R25/R26/R27`, TFM из props, Central Package Management.
- Протокол WebView2: Request / Response / Progress / Event с `id`.
- Правило «headless»: ядро без WPF, проверка границ в CI (`Check-Boundaries.ps1`).
- NUKE: сборка клиента → плагин → инсталлятор → релиз.
- Debug-сборка сама деплоится в `%AppData%\Autodesk\Revit\Addins\<year>\`.

Аналог для сравнения: Nice3point.Revit.Templates / Revit.Toolkit (там тоже есть Host + DI) — посмотреть перед фазой 2.

## 3. Структура решения (черновик)

```
Davydov.Revit.Template/
├─ src/
│  ├─ Davydov.Revit.Hosting/              «фреймворк» (аналог Microsoft.AspNetCore.*), headless
│  │  ├─ Builder/        RevitAppBuilder, RevitApp
│  │  ├─ Commands/       ICommand<,>, [Command], CommandContext, реестр, model binding
│  │  ├─ Pipeline/       ICommandMiddleware + встроенные middleware
│  │  ├─ Events/         RevitEventSource, IAppEventHandler<>, IRevitEventHandler<>, RevitUpdater, IClientEvents
│  │  ├─ Documents/      IDocumentRegistry, DocumentKey, DocumentRef, middleware DocumentBinding
│  │  ├─ Threading/      IRevitDispatcher (ExternalEvent)
│  │  └─ Transports/     ITransport
│  ├─ Davydov.Revit.Hosting.Generators/   source generator + анализаторы (netstandard2.0), раздел 4
│  ├─ Davydov.Revit.Hosting.Wpf/          WebView2-окно/док-панель, транспорт, лента
│  ├─ Davydov.Revit.Template/              само приложение (аналог проекта WebApplication)
│  │  ├─ Program.cs
│  │  ├─ appsettings.json
│  │  ├─ Commands/       «контроллеры», по фичам: Commands/Families/…
│  │  ├─ EventHandlers/  обработчики событий и апдейтеры, по фичам
│  │  ├─ Services/
│  │  ├─ Models/
│  │  └─ ClientApp/      React + Vite (см. раздел 7)
│  └─ Davydov.Revit.Template.Launcher/    тонкий IExternalApplication + ALC
├─ tests/                в т.ч. Davydov.Revit.Hosting.Generators.Tests (снимки + диагностики)
├─ build/                NUKE
├─ Directory.Build.props, Directory.Packages.props
└─ .template.config/     template.json (для dotnet new)
```

## 4. Команды

### Сериализация — System.Text.Json

- Везде STJ: протокол WebView2, model binding команд, `appsettings.json`, ответы. Newtonsoft не используется
  (в AnalyseTool он был в публичном контракте — `JToken`, `RevitPayload`).
- Одни общие `JsonSerializerOptions` в DI, настраиваются как в ASP.NET Core:
  `builder.Services.ConfigureJson(o => …)`. По умолчанию: camelCase, регистронезависимое чтение, enum как строки.
- Payload приходит как `JsonElement` и десериализуется в `TRequest` один раз, в конвейере (model binding).
- В Hosting — конвертеры для типов Revit, которые встречаются в DTO: `ElementId` ↔ `long` и т.п.
- Версия STJ — только та, что встроена в runtime (net8 для R25/26, net10 для R27), **без NuGet-пакета
  System.Text.Json**: иначе в изолированном ALC может загрузиться вторая копия, конфликтующая с той, что в Revit.
  Следствие: `JsonSchemaExporter` (нужен для MCP-схем) есть только с .NET 9, поэтому на R25/26 вопрос схем
  решаем в фазе 8.
- Newtonsoft допустим только если его требует сторонняя библиотека, и только внутри её обёртки.

### Имена команд

- По умолчанию имя команды = имя класса (`RenameFamily` → `"RenameFamily"`), атрибут `[Command]` не обязателен.
- `[Command("Name")]` — только чтобы переопределить имя (например, сохранить старое имя после переименования класса).
- Суффикс `Command` отбрасывается, как `Controller` в ASP.NET Core: `RenameFamilyCommand` → `"RenameFamily"`.
- Сравнение без учёта регистра.
- Конфликт имён (два класса с одинаковым именем в разных папках) — **ошибка компиляции** (генератор, ниже),
  а не тихий пропуск с записью в лог, как в AnalyseTool.
- Защита от переименований — генерация TS-типов из команд (фаза 7): переименовал класс → фронтенд не скомпилируется.

### Регистрация — source generator, без reflection

Команды и обработчики событий находит **source generator** при компиляции (в VS — прямо при наборе кода).
Reflection-сканирования нет вообще.

- Проект `Davydov.Revit.Hosting.Generators` (netstandard2.0, `IIncrementalGenerator`), подключается к приложению
  как анализатор: `<ProjectReference … OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`.
  Версия `Microsoft.CodeAnalysis.CSharp` — минимальная из поддерживаемых VS/SDK, закрепляется в CPM.
- Генератор находит не абстрактные классы, реализующие `ICommand<TResponse>` / `ICommand<TRequest, TResponse>`,
  `IAppEventHandler<T>`, `IRevitEventHandler<TArgs>`, и применяет правила имён (раздел выше). **Правила имён живут
  только в генераторе.**
- Генерирует в сборку приложения сами методы `AddCommands()` и `AddRevitEvents()` — Hosting их не объявляет.
  Не подключил генератор → `Program.cs` не компилируется, а не «плагин загрузился, команд нет».

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
  | `DRT010` | `ActiveUIDocument` в `Commands/`, `EventHandlers/`, `Services/` — использовать `context.Document` (раздел 6). Заменяет скрипт в CI |

- Команды только для отладки — обычный `#if DEBUG` вокруг класса: генератор видит символы препроцессора, в Release
  команды просто нет.
- Что генератор проверить не может — зарегистрированы ли в DI зависимости из конструкторов. Это остаётся
  в `Build()` (`ValidateOnBuild`).
- Тесты генератора — снимки сгенерированного кода (Verify.SourceGenerators) + тесты на каждую диагностику.
- Сгенерированный код виден в VS: Dependencies → Analyzers; в `obj/` — с `EmitCompilerGeneratedFiles=true` в Debug.

## 5. События Revit

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
public sealed record DocumentInfo(string Key, string Title, string PathName, bool IsFamily);

public sealed record ActiveDocumentChanged(DocumentInfo? Document);   // null — открытых документов нет
public sealed record DocumentOpened(DocumentInfo Document);
public sealed record DocumentClosed(string Title);
public sealed record DocumentSaved(DocumentInfo Document);
public sealed record SelectionChanged(DocumentInfo Document, IReadOnlyList<ElementId> ElementIds);
public sealed record ModelChanged(
    DocumentInfo Document,
    IReadOnlyList<ElementId> Added, IReadOnlyList<ElementId> Modified, IReadOnlyList<ElementId> Deleted,
    IReadOnlyList<string> Transactions,
    bool FromOurCommand);   // изменение сделала наша команда — чтобы фронтенд не обновлялся по кругу
```

`ActiveDocumentChanged` переносит проверенную логику `DocumentTracker`: сигнал — `ViewActivated` + `DocumentClosed`
(`DocumentOpened` срабатывает раньше, чем документ становится активным), смена вида внутри того же документа
отсекается сравнением ключа. `Key` — это `DocumentKey` из реестра открытых документов (раздел 6).

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
можно идти в модель), `ApplicationStopping` — в OnShutdown. `Idling` наружу не отдаётся: им пользуется только
Hosting (диспетчер, готовность док-панели).

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

## 6. Документы: пользователь может сменить активный документ в любой момент

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
   (и для уже открытых — на `ApplicationInitialized`), удаляет при `DocumentClosed`. Ключ не меняется после
   «Сохранить как» и не путает две копии с одинаковым именем (в отличие от `PathName|Title`). Связанные файлы —
   с пометкой `IsLinked`, менять их нельзя.
2. **Каждый запрос несёт документ.** Фронтенд добавляет его сам:
   `{"type":"request","id":"7","command":"RenameFamily","document":"d3f1","payload":{…}}`.
   Без документа (лента, MCP) — берётся активный **в момент вызова** и фиксируется на всю команду.
3. **Встроенное middleware `DocumentBinding`** (сразу после `ExceptionHandling`): находит документ по ключу
   (закрыт → ошибка `DocumentClosed`), проверяет требования команды, кладёт документ в `context.Document`,
   отменяет `context.CancellationToken` при `DocumentClosing` этого документа.
4. **Команда работает только с привязанным документом:** `revit.InvokeAsync(context.Document, doc => …)` находит
   документ заново в потоке Revit при каждом вызове — все шаги долгой команды идут в один документ; закрыт →
   `DocumentClosedException`.
5. **Требования — в атрибуте:**

   ```csharp
   [Command(RequiresActiveDocument = true)]       // выделение, виды, ShowElements — нужен UIDocument
   public sealed class SelectElements(IRevitDispatcher revit) : ICommand<SelectElementsRequest>
   {
       public Task ExecuteAsync(SelectElementsRequest request, CommandContext context) =>
           revit.InvokeAsync(context.Document, (UIDocument ui) => ui.Selection.SetElementIds(request.Ids));
   }

   [Command(DocumentKind = DocumentKind.Project)]  // в редакторе семейств — понятная ошибка
   public sealed class GetFamilies …
   ```

   Изменять модель можно и в неактивном документе (Revit API это позволяет, и пользователь смотрел именно на данные A).
   Действия с интерфейсом — только в активном, иначе отказ.
6. **Защита при компиляции:** `ActiveUIDocument` запрещён в `Commands/`, `EventHandlers/` и `Services/` —
   анализатор `DRT010` из проекта генератора (раздел 4). Единственный путь к документу — `context.Document`.

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
- Привязанное окно показывает документ в заголовке; документ закрыт — плашка «Документ закрыт», кнопки неактивны.
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

## 7. Фронтенд

Два слоя, чтобы фреймворк можно было сменить без изменений в C#:

1. **`revit-bridge`** — чистый TypeScript, без зависимостей от React. Переносится из `RevitBridge.ts`
   AnalyseTool: `invoke(command, payload, { onProgress, signal })`, подписка на события (`on("ActiveDocumentChanged", …)`),
   сопоставление Request/Response по `id`. В Debug без Revit (в обычном браузере) — mock-режим.
2. **React-приложение** поверх моста.

Предлагаемый стек:

| Задача | Выбор |
| --- | --- |
| Сборка | Vite + TypeScript |
| Компоненты | shadcn/ui (Radix) + Tailwind 4 — код компонентов копируется в `src/components/ui/` и принадлежит проекту |
| Таблицы на тысячи элементов | своя `<DataTable>` в шаблоне (`src/components/data-table/`): TanStack Table + TanStack Virtual + разметка shadcn; страницы описывают только колонки |
| Деревья (категории → семейства → типы) | в shadcn нет — своя `<TreeView>` в шаблоне или отдельная библиотека (решить в фазе 5) |
| Вызовы команд Revit | TanStack Query поверх `revit-bridge` (кэш, повторы, состояние загрузки) |
| Состояние UI | Zustand |
| Роутинг | React Router (hash/memory — страница открывается не с веб-сервера) |
| Графики / 3D | Recharts, three.js (через react-three-fiber при необходимости) |

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

## 8. Фазы

0. **Решения** — ответить на открытые вопросы ниже.
1. **Скелет** — решение, props, конфигурации R25–R27, CPM, Launcher + ALC, автодеплой.
   Готово, когда: в Revit появляется кнопка на ленте и пишет в лог.
2. **Hosting** — builder, DI, конфигурация, логирование, `IRevitDispatcher`, жизненный цикл hosted-сервисов.
   Готово, когда: сервис из DI читает документ через `IRevitDispatcher`.
3. **Команды** — source generator (`AddCommands`, диагностики `DRT001–003`, `DRT010`) + проверка DI при `Build()`,
   model binding, конвейер middleware, `IDocumentRegistry`, `DocumentBinding`, `context.Document`.
   Готово, когда: команда с DI и `TRequest` вызывается и проходит через middleware; команда с документом A
   выполняется в A при активном B; есть unit-тесты на конвейер.
4. **События** — `RevitEventSource`, события приложения, синхронные обработчики (регистрирует тот же генератор —
   `AddRevitEvents`), `RevitUpdater`, debounce, `IHostApplicationLifetime`.
   Готово, когда: смена документа и правка модели доходят до `IAppEventHandler`, апдейтер срабатывает,
   исключение в обработчике не роняет Revit.
5. **UI** — WebView2-окно и док-панель, транспорт, `revit-bridge` (TS), React ClientApp, Vite HMR в Debug,
   `ForwardToClient` / `useRevitEvent` / `refetchOn`, `RevitDocumentProvider` (follow/pinned),
   shadcn/ui + своя `<DataTable>` (сортировка, фильтры, выделение, виртуальный скролл).
   Готово, когда: кнопка в React вызывает команду и показывает результат и прогресс; при смене документа
   док-панель сама обновляется, а привязанное окно остаётся на своём документе.
6. **Сборка и выпуск** — NUKE, инсталлятор, CI, проверка границ.
7. **Шаблон** — `.template.config`, `dotnet new davydov-revit -n MyTool`, параметры; генерация TS-типов
   (отдельная утилита после сборки через `MetadataLoadContext` — source generator не может писать файлы в `ClientApp/`).
8. **Опционально** — MCP, Roslyn-скрипты (переносятся из AnalyseTool как модули `AddMcp()` и т.п.).
   Системы сторонних расширений нет (решение 12).

## 9. Открытые вопросы

- Деревья во фронтенде: своя `<TreeView>` или сторонняя библиотека? (фаза 5)

## 10. Решения

| # | Решение | Дата |
| --- | --- | --- |
| 1 | Hosting сначала живёт в исходниках шаблона; в NuGet выносим, когда API устоится. | 2026-10-24 |
| 2 | В базе только WebView2 + веб-фронтенд. MCP, расширения/SDK, Roslyn — позже, опциональными модулями (фаза 8). | 2026-10-24 |
| 3 | Раздача через `dotnet new` (`.template.config`, переименование по `sourceName`). | 2026-10-24 |
| 4 | Revit 2025–2027: net8 для 2025/2026, net10 для 2027. | 2026-10-24 |
| 5 | Фронтенд — React (вместо Vue из AnalyseTool). Мост к Revit — отдельный TS-модуль без привязки к фреймворку. | 2026-10-24 |
| 6 | Сериализация — System.Text.Json везде, версия из runtime (без NuGet-пакета). Newtonsoft — только внутри обёрток сторонних библиотек. | 2026-10-24 |
| 7 | Имя команды по умолчанию = имя класса; `[Command("…")]` — необязательное переопределение. | 2026-10-24 |
| 8 | События Revit — один механизм: события приложения (async), синхронные обработчики, `RevitUpdater`, пересылка во фронтенд. | 2026-10-24 |
| 9 | Документ — часть запроса: `DocumentKey` из реестра, middleware `DocumentBinding`, `context.Document` вместо `ActiveUIDocument`; окна follow/pinned. | 2026-10-24 |
| 10 | Один способ: команды и обработчики событий — только классы. Регистрация — один вызов `AddCommands()` (без `MapCommands`), проверки при `Build()`. Делегаты в стиле Minimal API (`MapCommand`, `app.On<T>`) — не делаем, пока не понадобятся. | 2026-10-24 |
| 11 | Компоненты — shadcn/ui + Tailwind 4 (не PrimeReact). Таблица — своя `<DataTable>` на TanStack Table + Virtual, собирается один раз в шаблоне. | 2026-10-24 |
| 12 | Системы сторонних расширений (загрузка чужих DLL, SDK-пакет, collectible ALC) нет — ни в базе, ни опционально. Hosting остаётся одним проектом, без отдельного Abstractions. | 2026-10-24 |
| 13 | Регистрация команд и обработчиков событий — source generator с фазы 3, без reflection. Генератор сам пишет `AddCommands()`/`AddRevitEvents()`, ловит ошибки при компиляции (`DRT001–003`), анализатор `DRT010` запрещает `ActiveUIDocument`. DI проверяется при `Build()`. | 2026-10-24 |
