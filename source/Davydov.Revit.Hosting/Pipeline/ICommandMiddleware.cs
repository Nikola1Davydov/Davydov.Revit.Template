using Davydov.Revit.Hosting.Commands;

namespace Davydov.Revit.Hosting.Pipeline;

/// <summary>
///     The next step of the pipeline: the next middleware or the command itself.
/// </summary>
public delegate Task<object?> CommandDelegate(CommandContext context);

/// <summary>
///     A layer around every command call, as ASP.NET Core middleware (plan, section 5.4).
/// </summary>
/// <remarks>
///     Runs outside the Revit thread and sees only the request (<see cref="CommandContext.Request" />): no business
///     logic, no model access. Throw <see cref="CommandException" /> to stop the call with an error.
/// </remarks>
/// <example>
///     <code>
///     public sealed class TimingMiddleware(ILogger&lt;TimingMiddleware&gt; logger) : ICommandMiddleware
///     {
///         public async Task&lt;object?&gt; InvokeAsync(CommandContext context, CommandDelegate next)
///         {
///             var stopwatch = Stopwatch.StartNew();
///             try { return await next(context); }
///             finally { logger.LogInformation("{Command}: {Ms} ms", context.CommandName, stopwatch.ElapsedMilliseconds); }
///         }
///     }
///     </code>
/// </example>
[PublicAPI]
public interface ICommandMiddleware
{
    /// <summary>Runs this layer; call <paramref name="next" /> to continue the pipeline.</summary>
    Task<object?> InvokeAsync(CommandContext context, CommandDelegate next);
}
