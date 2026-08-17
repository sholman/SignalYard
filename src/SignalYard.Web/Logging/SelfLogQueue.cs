using System.Threading.Channels;
using SignalYard.Core.Models;

namespace SignalYard.Web.Logging;

/// <summary>
/// Bounded in-memory buffer between the log provider (many producers, on arbitrary request threads) and
/// the background flush service (single consumer). It is bounded and non-blocking: when full, the newest
/// event is dropped and counted rather than blocking the caller, so self-logging never adds latency to a
/// request and memory stays capped even when storage is slow or unavailable.
/// </summary>
public sealed class SelfLogQueue
{
    private readonly Channel<ClefLogEvent> _channel;
    private long _dropped;

    public SelfLogQueue(SelfLoggingOptions options)
    {
        // FullMode.Wait combined with TryWrite (which we never await) gives us "drop the newest and tell
        // us about it": TryWrite returns false when the buffer is full instead of blocking. The other
        // Drop* modes silently succeed, which would hide the drop from DroppedCount.
        _channel = Channel.CreateBounded<ClefLogEvent>(new BoundedChannelOptions(Math.Max(1, options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <summary>Total events dropped because the buffer was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>Non-blocking enqueue. Returns false (and counts a drop) when the buffer is full.</summary>
    public bool TryEnqueue(ClefLogEvent evt)
    {
        if (_channel.Writer.TryWrite(evt))
        {
            return true;
        }

        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <summary>
    /// Reads the next batch, waiting for at least one event, up to <paramref name="maxBatch"/>. Returns an
    /// empty list only if the channel has been completed.
    /// </summary>
    public async Task<List<ClefLogEvent>> ReadBatchAsync(int maxBatch, CancellationToken cancellationToken)
    {
        var batch = new List<ClefLogEvent>();

        if (!await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            return batch;
        }

        while (batch.Count < maxBatch && _channel.Reader.TryRead(out var evt))
        {
            batch.Add(evt);
        }

        return batch;
    }

    /// <summary>Drains whatever is currently buffered without waiting. Used for a best-effort flush on shutdown.</summary>
    public List<ClefLogEvent> DrainRemaining()
    {
        var batch = new List<ClefLogEvent>();
        while (_channel.Reader.TryRead(out var evt))
        {
            batch.Add(evt);
        }

        return batch;
    }
}
