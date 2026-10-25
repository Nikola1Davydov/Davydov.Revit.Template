namespace Davydov.Revit.Addin.Launcher;

/// <summary>
///     Names the Launcher needs to start the add-in. Strings, not types: the Launcher must not reference add-in types.
/// </summary>
/// <remarks>The project template replaces these values when a new add-in is created.</remarks>
internal static class PluginInfo
{
    /// <summary>The add-in name, used for the load context, ribbon texts and the startup log folder.</summary>
    public const string Name = "Davydov.Revit.Addin";

    /// <summary>The add-in assembly file, next to the Launcher.</summary>
    public const string AssemblyFile = "Davydov.Revit.Addin.dll";

    /// <summary>The <c>IExternalApplication</c> class of the add-in.</summary>
    public const string ApplicationType = "Davydov.Revit.Addin.Application";

    /// <summary>
    ///     <see cref="AppContext" /> keys read by <c>Davydov.Revit.Hosting.Ribbon.LauncherInfo</c>; keep both sides in sync.
    ///     <see cref="AppContext" /> is process-wide, so the keys end with the add-in name: two add-ins made from the
    ///     template must not read each other's Launcher.
    /// </summary>
    public const string LauncherPathKey = $"Davydov.Revit.Launcher.Path:{Name}";

    /// <inheritdoc cref="LauncherPathKey" />
    public const string ProxyNamespaceKey = $"Davydov.Revit.Launcher.ProxyNamespace:{Name}";
}
