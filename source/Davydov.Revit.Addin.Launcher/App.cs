using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using Davydov.Revit.Addin.Launcher.Fallback;
using Davydov.Revit.Addin.Launcher.Proxies;

namespace Davydov.Revit.Addin.Launcher;

/// <summary>
///     The entry point Revit loads. Loads the add-in into its own <see cref="PluginLoadContext" />
///     and forwards startup and shutdown to the add-in application created inside it.
/// </summary>
public sealed class App : IExternalApplication
{
    private static IExternalApplication? _application;

    /// <summary>Gets the isolated context of the add-in, or <see langword="null" /> when it failed to load.</summary>
    internal static PluginLoadContext? PluginContext { get; private set; }

    /// <inheritdoc />
    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            var launcherPath = typeof(App).Assembly.Location;
            AppContext.SetData(PluginInfo.LauncherPathKey, launcherPath);
            AppContext.SetData(PluginInfo.ProxyNamespaceKey, typeof(ProxyCommand).Namespace);

            // The Launcher's deps.json lists the add-in and all its packages (referenced projects get no deps.json of their own)
            var pluginPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, PluginInfo.AssemblyFile);
            PluginContext = new PluginLoadContext(PluginInfo.Name, launcherPath);

            var pluginAssembly = PluginContext.LoadFromAssemblyPath(pluginPath);
            var applicationType = pluginAssembly.GetType(PluginInfo.ApplicationType, throwOnError: true)!;

            // The single reflection call into the add-in; after this, Revit talks to it through IExternalApplication
            _application = (IExternalApplication)Activator.CreateInstance(applicationType)!;

            var result = _application.OnStartup(application);
            if (result != Result.Succeeded)
            {
                FallbackRibbon.Create(application, StartupLog.Write(application, $"{PluginInfo.ApplicationType}.OnStartup returned {result}."));
            }
        }
        catch (Exception exception)
        {
            var actual = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            FallbackRibbon.Create(application, StartupLog.Write(application, actual.ToString()));
        }

        // The fallback panel needs a loaded Launcher, so startup never reports failure to Revit
        return Result.Succeeded;
    }

    /// <inheritdoc />
    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            return _application?.OnShutdown(application) ?? Result.Succeeded;
        }
        catch (Exception exception)
        {
            StartupLog.Write(application, $"Shutdown failed:{Environment.NewLine}{exception}");
            return Result.Succeeded;
        }
    }
}
