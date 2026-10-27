using System.Text.Json;
using Davydov.Revit.Hosting.Commands;
using Davydov.Revit.Hosting.Mcp;
using Davydov.Revit.Hosting.Scripting.Execution;

namespace Davydov.Revit.Hosting.Scripting;

/// <summary>
///     Compiles C# with Roslyn and runs it once inside Revit — the scratchpad of an AI client, and of the Dev page.
/// </summary>
/// <remarks>
///     Registered only while scripting is on (<c>AddScripting</c>): off, the command does not exist, so neither MCP nor a page
///     can reach it. Arbitrary code runs with the user's rights and may change the model, hence <c>Destructive</c>.
/// </remarks>
[McpTool(Destructive = true, Title = "Execute C# in Revit")]
[Command(Description =
    "Compiles a C# method body with Roslyn and runs it once on the Revit thread, returning its result. " +
    "In scope: uiapp (UIApplication), uidoc (UIDocument, null without an open document), doc (Document, may be null) " +
    "and cancellationToken; Autodesk.Revit.DB, Autodesk.Revit.UI, System.Linq and System.Collections.Generic are imported, " +
    "other `using` directives may come on the first lines. `return` plain data — numbers, strings, lists, anonymous objects — " +
    "not Revit elements: return ids and names instead. To change the model open a Transaction yourself. " +
    "Check unfamiliar types with DescribeApi first: the API differs between Revit versions. " +
    "Revit is frozen while the code runs: keep it short. Errors come back as compile_failed or script_failed with the line of the code.")]
internal sealed class ExecuteCode(ScriptRunner runner) : ICommand<ExecuteCodeRequest, JsonElement>
{
    /// <inheritdoc />
    public Task<JsonElement> ExecuteAsync(ExecuteCodeRequest request, CommandContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new CommandException(CommandErrorCodes.BadRequest, "There is no code to run.");
        }

        return runner.RunAsync(request.Code, context.CancellationToken);
    }
}

/// <summary>The input of <see cref="ExecuteCode" />.</summary>
/// <param name="Code">The C# method body; it may <c>return</c> a value.</param>
internal sealed record ExecuteCodeRequest(string Code);
