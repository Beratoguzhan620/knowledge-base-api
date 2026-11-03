using System.Threading.Channels;

namespace KnowledgeBase.Api.Features.Indexing;

/// <summary>Wakes the indexer as soon as a document is queued, instead of on its next poll.</summary>
internal sealed class IndexingSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <returns>When notified, or when <paramref name="timeout"/> has passed.</returns>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timer.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(timer.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timed out: time to poll.
        }
    }
}
