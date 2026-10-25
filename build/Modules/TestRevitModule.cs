using ModularPipelines.Attributes;
using ModularPipelines.Conditions;
using ModularPipelines.Context;
using ModularPipelines.DotNet.Extensions;
using ModularPipelines.DotNet.Options;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Run the tests inside Revit (Davydov.Revit.Addin.Tests) for each supported Revit configuration.
/// </summary>
/// <remarks>
///     Nice3point.TUnit.Revit starts the installed Revit of the configuration, so not in CI: locally,
///     with Revit closed. Our tests only, not part of the template.
/// </remarks>
[SkipIf<IsCI>]
[DependsOn<ResolveConfigurationsModule>]
[DependsOn<TestProjectModule>]
public sealed class TestRevitModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var configurationsResult = await context.GetModule<ResolveConfigurationsModule>();
        var configurations = configurationsResult.ValueOrDefault!;

        foreach (var configuration in configurations)
        {
            await context.SubModule(configuration, async () => await context.DotNet().Test(new DotNetTestOptions
            {
                Project = Projects.Davydov_Revit_Addin_Tests.FullName,
                Configuration = configuration
            }, cancellationToken: cancellationToken));
        }
    }
}
