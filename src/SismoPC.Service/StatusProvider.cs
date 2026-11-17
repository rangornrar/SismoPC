using SismoPC.Core.Ipc;

namespace SismoPC.Service;

/// <summary>Dernier état connu du PC, mis à jour par la collecte et lu par le canal nommé.</summary>
public sealed class StatusProvider
{
    private volatile LiveStatus? _current;

    public LiveStatus? Current => _current;

    public void Update(LiveStatus status) => _current = status;
}
