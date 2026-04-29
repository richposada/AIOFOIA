using System.Threading.Channels;

namespace FoiaProcessor.Agents.Workflow;

/// <summary>
/// In-memory FIFO queue of FOIA request ids the workflow runner should process.
/// Singleton; shared by API controllers (producers) and
/// <see cref="WorkflowHostedService"/> (consumer).
/// </summary>
public class WorkflowQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(Guid requestId, CancellationToken ct = default)
        => _channel.Writer.WriteAsync(requestId, ct);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
