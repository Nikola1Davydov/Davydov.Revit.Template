using System.Collections.Concurrent;
using Davydov.Revit.Hosting.Commands;

namespace Davydov.Revit.Hosting.Pipeline;

/// <summary>
///     Refuses a second call of a command while the first one is still running — a double click
///     (<c>UseSingleFlight</c>, plan, section 5.4).
/// </summary>
public sealed class SingleFlightMiddleware : ICommandMiddleware
{
    private readonly ConcurrentDictionary<string, byte> _running = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<object?> InvokeAsync(CommandContext context, CommandDelegate next)
    {
        if (!_running.TryAdd(context.CommandName, 0))
        {
            throw new CommandException(CommandErrorCodes.AlreadyRunning, $"'{context.CommandName}' is already running.");
        }

        try
        {
            return await next(context).ConfigureAwait(false);
        }
        finally
        {
            _running.TryRemove(context.CommandName, out _);
        }
    }
}
