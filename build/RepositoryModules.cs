using Build.Modules;
using Microsoft.Extensions.DependencyInjection;
using ModularPipelines.Extensions;

/// <summary>
///     The modules of this repository only; the template leaves this file out.
/// </summary>
internal partial class Program
{
    /// <summary>
    ///     Always: the sync of the template solution for Visual Studio. With the test argument: the tests without Revit,
    ///     the Vitest tests of revit-bridge and the tests inside Revit.
    /// </summary>
    static partial void AddRepositoryModules(IServiceCollection services, string[] args)
    {
        services.AddModule<TemplateSolutionModule>();

        if (!args.Contains("test")) return;

        services.AddModule<TestProjectModule>();
        services.AddModule<TestRevitModule>();
        services.AddModule<TestRevitBridgeModule>();
    }
}
