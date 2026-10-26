#if DEBUG
using Davydov.Revit.Addin.Infrastructure.Diagnostics;
using Davydov.Revit.Hosting.Commands;

namespace Davydov.Revit.Addin.Features.Dev;

/// <summary>
///     Debug-only command of the «Dev» page: returns the log records written since the caller's last call. The page polls it;
///     <see cref="LogBuffer" /> drops the records of the polling itself.
/// </summary>
[Command(Description = "Returns the log records after the given sequence number, oldest first (Debug only).")]
public sealed class GetLogEntries(LogBuffer buffer) : ICommand<GetLogEntriesRequest, LogPage>
{
    /// <inheritdoc />
    public Task<LogPage> ExecuteAsync(GetLogEntriesRequest request, CommandContext context)
    {
        var entries = buffer.GetAfter(request.AfterSequence);
        var last = entries.Count > 0 ? entries[^1].Sequence : request.AfterSequence;
        return Task.FromResult(new LogPage(entries, last));
    }
}

/// <summary>The input of <see cref="GetLogEntries" />.</summary>
/// <param name="AfterSequence">The <see cref="LogEntry.Sequence" /> of the last record the caller has; <c>0</c> for all.</param>
public sealed record GetLogEntriesRequest(long AfterSequence);

/// <summary>The result of <see cref="GetLogEntries" />.</summary>
/// <param name="Entries">The new records, oldest first; at most <see cref="LogBuffer.Capacity" />.</param>
/// <param name="LastSequence">What to pass as <see cref="GetLogEntriesRequest.AfterSequence" /> next time.</param>
public sealed record LogPage(IReadOnlyList<LogEntry> Entries, long LastSequence);
#endif
