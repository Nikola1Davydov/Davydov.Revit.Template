using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Run the tests without Revit (Davydov.Revit.Hosting.Tests) for each supported Revit configuration, in CI as well.
/// </summary>
/// <remarks>Our tests only, not part of the template.</remarks>
[DependsOn<ResolveConfigurationsModule>]
[DependsOn<CompileProjectModule>]
public sealed class TestProjectModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var configurationsResult = await context.GetModule<ResolveConfigurationsModule>();
        var configurations = configurationsResult.ValueOrDefault!;

        foreach (var configuration in configurations)
        {
            await context.SubModule(configuration, async () => await context.DotNet().Test(new DotNetTestOptions
            {
                Project = Projects.Davydov_Revit_Hosting_Tests.FullName,
                Configuration = configuration
            }, cancellationToken: cancellationToken));
        }
    }
}
