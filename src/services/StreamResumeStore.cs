using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Services;

public interface IStreamResumeStore
{
    void Record(string streamKey, string lastEventId);
    bool TryGet(string streamKey, [MaybeNullWhen(false)] out string lastEventId);
}

public sealed class InMemoryStreamResumeStore : IStreamResumeStore
{
    readonly ConcurrentDictionary<string, string> _state = new();
    public void Record(string streamKey, string lastEventId) => _state[streamKey] = lastEventId;
    public bool TryGet(string streamKey, [MaybeNullWhen(false)] out string lastEventId) => _state.TryGetValue(streamKey, out lastEventId!);
}
