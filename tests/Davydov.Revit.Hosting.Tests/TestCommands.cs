using Davydov.Revit.Hosting.Commands;

namespace Davydov.Revit.Hosting.Tests;

// Command classes for the tests. They are registered explicitly with FromTypes, never by scanning this assembly:
// some of them are broken on purpose.

public sealed record GreetRequest(string Name);

[Command(Description = "Greets by name.")]
public sealed class GreetCommand : ICommand<GreetRequest, string>
{
    public Task<string> ExecuteAsync(GreetRequest request, CommandContext context)
    {
        return Task.FromResult($"Hello, {request.Name}");
    }
}

public sealed class DoNothing : ICommand<Empty>
{
    public Task<Empty> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(Empty.Value);
    }
}

[Command("Renamed")]
public sealed class OldNameCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        return Task.FromResult(42);
    }
}

public sealed class RefuseCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        throw new CommandException("refused", "Not today.");
    }
}

public sealed class CrashCommand : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context)
    {
        throw new InvalidOperationException("Boom.");
    }
}

/// <summary>Records what it saw and waits for <see cref="Gate" /> — for concurrency and middleware tests.</summary>
public sealed class ProbeCommand(Recorder recorder) : ICommand<string>
{
    public async Task<string> ExecuteAsync(CommandContext context)
    {
        recorder.Add($"command:{context.Source}");
        await recorder.Gate.Task.WaitAsync(context.CancellationToken);
        return "done";
    }
}

public sealed class Recorder
{
    private readonly List<string> _entries = [];

    public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<string> Ran { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public void Add(string entry)
    {
        lock (_entries)
        {
            _entries.Add(entry);
        }

        Ran.TrySetResult(entry);
    }
}

// Broken commands: each one must stop the registration

public sealed class DuplicateA : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(1);
}

[Command("DuplicateA")]
public sealed class DuplicateB : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(2);
}

[Command("My.Command")]
public sealed class DottedName : ICommand<int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(0);
}

public abstract record AbstractRequest;

public sealed class AbstractRequestCommand : ICommand<AbstractRequest, int>
{
    public Task<int> ExecuteAsync(AbstractRequest request, CommandContext context) => Task.FromResult(0);
}

public sealed class TwoInterfaces : ICommand<int>, ICommand<string, int>
{
    public Task<int> ExecuteAsync(CommandContext context) => Task.FromResult(0);

    public Task<int> ExecuteAsync(string request, CommandContext context) => Task.FromResult(0);
}
