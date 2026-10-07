using System.Collections;
using NAP.Core;

namespace NAP.Presentation;

/// <summary>Index-based virtual list. Only requested pages get placeholders; four-page LRU, no full enumeration in the UI.</summary>
public sealed class PagedAssetCollection : IList, IDisposable
{
    public const int PageSize = 60;
    public const int PageCapacity = 4;
    private readonly IExplorerService _service;
    private readonly UniverseContext _context;
    private readonly CatalogFilter _filter;
    private readonly CancellationTokenSource _lifetime = new();
    private sealed class PageEntry(AssetTileViewModel[] items, long used, CancellationToken lifetime) : IDisposable
    {
        public AssetTileViewModel[] Items { get; } = items;
        public long Used { get; set; } = used;
        public CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        private bool _disposed;
        public void Dispose() { if (_disposed) return; _disposed = true; Cancellation.Cancel(); Cancellation.Dispose(); foreach (var item in Items) item.Dispose(); }
    }
    private readonly Dictionary<int, PageEntry> _pages = [];
    private long _clock;
    private readonly Action<Exception> _onError;
    public PagedAssetCollection(IExplorerService service, UniverseContext context, CatalogFilter filter, CatalogPage first, Action<Exception> onError)
    {
        _service = service; _context = context; _filter = filter; _onError = onError;
        Count = checked((int)first.TotalCount);
        if (Count > 0) Create(0, first);
    }
    public int CachedPages => _pages.Count;
    public int Count { get; }
    public object? this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            var page = index / PageSize;
            if (!_pages.TryGetValue(page, out var cached)) { Create(page, null); cached = _pages[page]; }
            cached.Used = ++_clock; return cached.Items[index % PageSize];
        }
        set => throw new NotSupportedException();
    }
    private void Create(int page, CatalogPage? initial)
    {
        if (_pages.Count == PageCapacity)
        {
            var oldest = _pages.MinBy(p => p.Value.Used); oldest.Value.Dispose(); _pages.Remove(oldest.Key);
        }
        var items = Enumerable.Range(0, Math.Min(PageSize, Count - page * PageSize)).Select(_ => new AssetTileViewModel(_service, _context)).ToArray();
        var entry = new PageEntry(items, ++_clock, _lifetime.Token); _pages.Add(page, entry);
        if (initial is not null) Fill(items, initial); else _ = LoadAsync(page, entry, entry.Cancellation.Token);
    }
    private static void Fill(AssetTileViewModel[] items, CatalogPage result)
    { for (var i = 0; i < Math.Min(items.Length, result.Items.Count); i++) items[i].Populate(result.Items[i]); }
    private async Task LoadAsync(int page, PageEntry entry, CancellationToken cancellation)
    {
        try
        {
            var result = await _service.PageAsync(_context, _filter, page * PageSize, PageSize, cancellation);
            if (!cancellation.IsCancellationRequested && _pages.TryGetValue(page, out var cached) && ReferenceEquals(entry, cached))
            {
                if (result.TotalCount != Count) throw new InvalidDataException("Catalog changed; refresh explicitly.");
                Fill(entry.Items, result);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!cancellation.IsCancellationRequested) { foreach (var item in entry.Items) item.Fail(ex); _onError(ex); } }
    }
    public int IndexOf(object? value)
    { foreach (var page in _pages) { var i = Array.IndexOf(page.Value.Items, value); if (i >= 0) return page.Key * PageSize + i; } return -1; }
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (var i = 0; i < Count; i++) yield return this[i]; }
    public void CopyTo(Array array, int index) { for (var i = 0; i < Count; i++) array.SetValue(this[i], index + i); }
    public bool IsFixedSize => true;
    public bool IsReadOnly => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    private bool _disposed;
    public void Dispose() { if (_disposed) return; _disposed = true; _lifetime.Cancel(); foreach (var page in _pages.Values) page.Dispose(); _pages.Clear(); _lifetime.Dispose(); }
}
