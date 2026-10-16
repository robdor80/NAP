namespace NAP.Core;

/// <summary>Private parsing reuse only. Callers must still validate every field and relationship.
/// Values never escape store validation; public reads deserialize their own independent objects.</summary>
internal sealed class QueueValidationPayloadCache
{
    // One process budget, rather than multiplying a cache by every store and universe.
    // A 1000-item admission ledger exceeds 8192 payloads; a smaller LRU continuously
    // evicts the beginning of a validation scan before it can be reused on the next scan.
    private const int MaxEntries = 32768;
    private const int MaxTextBytes = 32 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<(Type Type, string Json), LinkedListNode<Entry>> _entries = [];
    private static readonly LinkedList<Entry> _recent = [];
    private static int _textBytes;
    private sealed record Entry((Type Type, string Json) Key, object Value, int Bytes);

    internal T Decode<T>(string json) where T : class
    {
        lock (Gate)
        {
            var key = (typeof(T), json);
            if (_entries.TryGetValue(key, out var cached))
            {
                _recent.Remove(cached); _recent.AddLast(cached);
                return (T)cached.Value.Value;
            }
            var value = AutomationJson.Decode<T>(json); // Duplicate/canonical/format checks on every new byte sequence.
            var bytes = checked(json.Length * sizeof(char));
            if (bytes > MaxTextBytes) return value;
            while (_entries.Count >= MaxEntries || _textBytes + bytes > MaxTextBytes)
            {
                var first = _recent.First!;
                _entries.Remove(first.Value.Key); _textBytes -= first.Value.Bytes; _recent.RemoveFirst();
            }
            _entries.Add(key, _recent.AddLast(new Entry(key, value, bytes))); _textBytes += bytes;
            return value;
        }
    }
}
