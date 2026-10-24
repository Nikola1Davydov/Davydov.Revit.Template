using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Path = System.IO.Path;

namespace Davydov.Revit.Addin;

/// <summary>
///     Provides a host for the application's services and manages their lifetimes.
/// </summary>
public static class Host
{
    private static IHost? _host;

    /// <summary>
    ///     Registers the add-in services and starts the host.
    /// </summary>
    /// <returns>A task that represents the asynchronous host startup operation.</returns>
    public static async Task StartAsync()
    {
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

        _host = builder.Build();
        await _host.StartAsync();
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
}
