using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using ModularPipelines.Attributes;
using ModularPipelines.Context;
using ModularPipelines.Git.Extensions;
using ModularPipelines.Modules;
using Sourcy.DotNet;

namespace Build.Modules;

/// <summary>
///     Keeps <c>template/content/Davydov.Revit.Addin.sln</c> equal to the <c>.slnx</c> of this repository without its
///     repository-only entries.
/// </summary>
/// <remarks>
///     The «New project» dialog of Visual Studio replaces a <c>.slnx</c> of a solution template with a solution of its own,
///     so a project created there gets this classic <c>.sln</c> instead; <c>dotnet new</c> keeps the <c>.slnx</c>.
///     Locally the module rewrites the file; in CI it fails when the committed file is out of date.
///     The repository-only entries are the <c>&lt;!--#if (false)--&gt;</c> blocks of the <c>.slnx</c>: a new one goes here too.
///     This repository only, not part of the template. Runs after <see cref="ResolveConfigurationsModule" />: that module opens
///     the <c>.slnx</c> exclusively.
/// </remarks>
[DependsOn<ResolveConfigurationsModule>]
public sealed partial class TemplateSolutionModule : Module
{
    protected override async Task ExecuteModuleAsync(IModuleContext context, CancellationToken cancellationToken)
    {
        var model = await SolutionSerializers.SlnXml.OpenAsync(Solutions.Davydov_Revit_Addin.FullName, cancellationToken);
        RemoveRepositoryEntries(model);

        var temporaryFile = Path.GetTempFileName();
        await SolutionSerializers.SlnFileV12.SaveAsync(temporaryFile, model, cancellationToken);
        var expected = (await File.ReadAllTextAsync(temporaryFile, cancellationToken)).ReplaceLineEndings("\n");
        File.Delete(temporaryFile);

        var templateSolution = context.Git().RootDirectory.GetFolder("template").GetFolder("content").GetFile("Davydov.Revit.Addin.sln");
        var actual = templateSolution.Exists ? (await File.ReadAllTextAsync(templateSolution.Path, cancellationToken)).ReplaceLineEndings("\n") : null;
        if (actual == expected) return;

        var isCi = bool.TryParse(Environment.GetEnvironmentVariable("CI"), out var ci) && ci;
        if (isCi)
        {
            throw new InvalidOperationException(
                $"Cannot use the template solution. '{templateSolution.Path}' is out of date with the .slnx file. " +
                "Run 'dotnet run -c Release --project build' locally and commit the updated file.");
        }

        await File.WriteAllTextAsync(templateSolution.Path, expected, cancellationToken);
        LogUpdated(context.Logger, templateSolution.Path);
    }

    /// <summary>
    ///     Removes the folders <c>Docs</c> and <c>Tests</c> with their projects and the template package project.
    /// </summary>
    private static void RemoveRepositoryEntries(SolutionModel model)
    {
        var repositoryFolders = model.SolutionFolders
            .Where(folder => folder.Path is "/Docs/" or "/Tests/")
            .ToList();

        foreach (var folder in repositoryFolders)
        {
            foreach (var project in model.SolutionProjects.Where(project => project.Parent == folder).ToList())
            {
                model.RemoveProject(project);
            }

            model.RemoveFolder(folder);
        }

        var templateProjects = model.SolutionProjects
            .Where(project => project.FilePath.StartsWith("template", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var project in templateProjects)
        {
            model.RemoveProject(project);
        }
    }

    [LoggerMessage(LogLevel.Information, "Updated the template solution {Path} from the .slnx file")]
    private static partial void LogUpdated(ILogger logger, string path);
}
