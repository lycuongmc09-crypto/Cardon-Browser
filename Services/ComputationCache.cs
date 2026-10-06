namespace KmyBrowser.Services;

// Computational Reuse Graph — tầng host (không chạm được Blink internals).
// Cache KẾT QUẢ TÍNH TOÁN trong RAM (không phải file):
//  - Resolve input -> URL, search URL, HTML trang nội bộ, kết quả filter history.
// LRU có giới hạn + đếm hit/miss để đo hiệu quả reuse.
public sealed class ComputationCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map = new();
    private readonly LinkedList<Entry> _lru = new();
    private readonly object _lock = new();

    private sealed record Entry(TKey Key, TValue Value);

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Count { get { lock (_lock) return _map.Count; } }
    public double HitRate => (Hits + Misses) == 0 ? 0 : (double)Hits / (Hits + Misses);

    public ComputationCache(int capacity = 200)
    {
        _capacity = Math.Max(1, capacity);
    }

    public bool TryGet(TKey key, out TValue? value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                Hits++;
                value = node.Value.Value;
                return true;
            }
            Misses++;
            value = default;
            return false;
        }
    }

    public void Put(TKey key, TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
            }
            else if (_map.Count >= _capacity)
            {
                var last = _lru.Last;
                if (last is not null)
                {
                    _map.Remove(last.Value.Key);
                    _lru.RemoveLast();
                }
            }
            var node = new LinkedListNode<Entry>(new Entry(key, value));
            _lru.AddFirst(node);
            _map[key] = node;
        }
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        if (TryGet(key, out var v) && v is not null) return v;
        var created = factory(key);
        Put(key, created);
        return created;
    }

    public void Clear()
    {
        lock (_lock) { _map.Clear(); _lru.Clear(); Hits = Misses = 0; }
    }
}
