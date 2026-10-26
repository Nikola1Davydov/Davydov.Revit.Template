using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.UI;
using Davydov.Revit.Hosting.Commands;
using Davydov.Revit.Hosting.Scripting.Compilation;
using Davydov.Revit.Hosting.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Davydov.Revit.Hosting.Scripting.Execution;

/// <summary>
///     The error codes of a script run, besides the pipeline's own.
/// </summary>
[PublicAPI]
public static class ScriptErrorCodes
{
    /// <summary>The code does not compile; the message lists the errors with their lines.</summary>
    public const string CompileFailed = "compile_failed";

    /// <summary>The code threw; the message names the exception and the line of the script.</summary>
    public const string ScriptFailed = "script_failed";
}

/// <summary>
///     Compiles a script on the thread pool, runs it once on the Revit thread and returns its result as JSON.
/// </summary>
/// <remarks>
///     The result is serialized on the Revit thread too: reading a property of a Revit object anywhere else is not allowed.
///     What cannot be serialized — a Revit object with a property that throws — comes back as its <c>ToString()</c>.
/// </remarks>
internal sealed partial class ScriptRunner(
    IRevitDispatcher dispatcher,
    IOptions<JsonSerializerOptions> serializerOptions,
    ILogger<ScriptRunner> logger)
{
    /// <summary>The add-in's load context in Revit; the default one in tests.</summary>
    private static readonly AssemblyLoadContext Owner = AssemblyLoadContext.GetLoadContext(typeof(ScriptRunner).Assembly) ?? AssemblyLoadContext.Default;

    // The references are collected once, at the first run: a few hundred files
    private readonly Lazy<ScriptCompiler> _compiler = new(() => new ScriptCompiler(ScriptReferences.Collect(Owner)));

    private JsonSerializerOptions? _resultOptions;

    public async Task<JsonElement> RunAsync(string code, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var compilation = _compiler.Value.Compile(code);
        if (!compilation.Succeeded)
        {
            throw new CommandException(ScriptErrorCodes.CompileFailed, string.Join(Environment.NewLine, compilation.Errors));
        }

        LogCompiled(logger, code.Length, stopwatch.ElapsedMilliseconds);

        var context = new ScriptLoadContext(Owner);
        try
        {
            var assembly = context.LoadFromStream(new MemoryStream(compilation.Assembly!), new MemoryStream(compilation.Pdb!));
            var execute = assembly.GetType(ScriptCompiler.TypeName)!.GetMethod(ScriptCompiler.MethodName)!;
            var run = execute.CreateDelegate<Func<UIApplication, CancellationToken, object?>>();

            return await dispatcher.InvokeAsync(application => Run(run, application, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Unload();
        }
    }

    private JsonElement Run(Func<UIApplication, CancellationToken, object?> run, UIApplication application, CancellationToken cancellationToken)
    {
        object? value;
        try
        {
            value = run(application, cancellationToken);
        }
        catch (Exception exception)
        {
            throw new CommandException(ScriptErrorCodes.ScriptFailed, Describe(exception));
        }

        return ToJson(value);
    }

    private JsonElement ToJson(object? value)
    {
        _resultOptions ??= new JsonSerializerOptions(serializerOptions.Value) { ReferenceHandler = ReferenceHandler.IgnoreCycles, MaxDepth = 16 };

        try
        {
            return JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object), _resultOptions);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogNotSerializable(logger, exception, value!.GetType().FullName);
            return JsonSerializer.SerializeToElement(value.ToString());
        }
    }

    /// <summary>The exception and the line of the script it came from — the PDB maps the frames to <c>script.cs</c>.</summary>
    private static string Describe(Exception exception)
    {
        var frame = exception.StackTrace?
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Contains(ScriptCompiler.FileName, StringComparison.Ordinal));

        var where = frame is null ? "" : $" ({frame[(frame.LastIndexOf(ScriptCompiler.FileName, StringComparison.Ordinal))..]})";
        return $"{exception.GetType().Name}: {exception.Message}{where}";
    }

    [LoggerMessage(LogLevel.Debug, "Script of {Length} characters compiled in {ElapsedMilliseconds} ms")]
    private static partial void LogCompiled(ILogger logger, int length, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Debug, "The result of a script ({Type}) cannot be serialized; it is returned as text")]
    private static partial void LogNotSerializable(ILogger logger, Exception exception, string? type);
}
