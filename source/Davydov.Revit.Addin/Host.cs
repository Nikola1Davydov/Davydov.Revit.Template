using System.Diagnostics;
using System.Reflection;
using Autodesk.Revit.UI;
using Davydov.Revit.Addin.Services.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
#if DEBUG
using Davydov.Revit.Addin.Infrastructure.Diagnostics;
#endif
using Path = System.IO.Path;

namespace Davydov.Revit.Addin;

/// <summary>
///     Provides a host for the application's services and manages their lifetimes.
/// </summary>
public static partial class Host
{
    private static IHost? _host;

    /// <summary>
    ///     Registers the add-in services and starts the host.
    /// </summary>
    /// <param name="application">The application Revit passed to the add-in at startup.</param>
    /// <returns>A task that represents the asynchronous host startup operation.</returns>
    public static async Task StartAsync(UIControlledApplication application)
    {
        var stopwatch = Stopwatch.StartNew();

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "Davydov.Revit.Addin",
            ContentRootPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
#if DEBUG
            EnvironmentName = Environments.Development,
#else
            EnvironmentName = Environments.Production,
#endif
            DisableDefaults = true
        });

        //Service defaults
        builder.AddServiceDefaults();

        //Davydov.Revit.Hosting: dispatcher, Revit lifetime, notifications. Before the first await — see AddRevitHosting
        builder.AddRevitHosting(application);

        //Commands and their pipeline (plan, section 5); the document rule is the add-in's own (section 7)
        builder.Services.AddScoped<ActiveDocument>();
        builder.AddRevitCommands(commands => commands
            .FromAssemblyOf<Application>()
            .UseCommandLogging()
            .Use<DocumentCheckMiddleware>());

        //Davydov.Revit.Hosting.WebView: windows and the dock pane with the ClientApp pages (plan, section 9)
        builder.AddWebView(application, webView => webView
            .DockPane(new Guid("CA1A2580-51FE-4199-A6B5-62760DC2D3CC"), "Davydov.Revit.Addin", "/dock")
            .AllowAnyNavigation());

#if DEBUG
        builder.Services.AddHostedService<StartupProbe>();
        builder.Services.Configure<DevOptions>(builder.Configuration.GetSection(DevOptions.SectionName));
#endif

        _host = builder.Build();
        await _host.StartAsync();

        LogHostStarted(_host.Services.GetRequiredService<ILogger<Application>>(), stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    ///     Stops the host and its <see cref="IHostedService" /> services.
    /// </summary>
    /// <returns>A task that represents the asynchronous host shutdown operation.</returns>
    public static async Task StopAsync()
    {
        if (_host is null)
        {
            return;
        }

        await _host.StopAsync();
    }

    /// <summary>
    ///     Gets the service of the specified type from the add-in service provider.
    /// </summary>
    /// <typeparam name="T">The type of service to resolve.</typeparam>
    /// <returns>The service instance.</returns>
    /// <exception cref="System.InvalidOperationException">No service of type <typeparamref name="T" /> is registered.</exception>
    /// <remarks>
    ///     Only for ribbon buttons: Revit creates <c>ExternalCommand</c> classes itself, so they cannot take
    ///     constructor dependencies (plan, section 4.1). Everything else uses constructor injection.
    /// </remarks>
    public static T GetService<T>() where T : class
    {
        return _host!.Services.GetRequiredService<T>();
    }

    [LoggerMessage(LogLevel.Information, "Host started in {ElapsedMilliseconds} ms")]
    private static partial void LogHostStarted(ILogger logger, long elapsedMilliseconds);
}
