using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace SismoPC.Core.Ipc;

/// <summary>
/// Diffuse les événements du service à tous les abonnés connectés.
/// Chaque abonné a sa file ; si un abonné ne lit plus, ses événements les plus anciens sont perdus, jamais ceux des autres.
/// </summary>
public sealed class EventHub
{
    private readonly Lock _lock = new();
    private readonly List<Channel<PipeEvent>> _subscribers = [];

    public int SubscriberCount
    {
        get
        {
            lock (_lock)
            {
                return _subscribers.Count;
            }
        }
    }

    public void Publish(PipeEvent e)
    {
        lock (_lock)
        {
            foreach (var channel in _subscribers)
            {
                channel.Writer.TryWrite(e);
            }
        }
    }

    public async IAsyncEnumerable<PipeEvent> Subscribe([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<PipeEvent>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        lock (_lock)
        {
            _subscribers.Add(channel);
        }

        try
        {
            await foreach (var e in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return e;
            }
        }
        finally
        {
            lock (_lock)
            {
                _subscribers.Remove(channel);
            }
        }
    }
}
