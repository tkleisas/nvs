using System.Collections.Concurrent;

namespace NVS.Services.Terminal;

/// <summary>
/// Minimal <see cref="IObservable{T}"/> / <see cref="IObserver{T}"/> implementation —
/// a poor man's <c>Subject</c> with zero dependencies on System.Reactive. Thread-safe
/// subscribe; OnNext/OnCompleted/OnError fan out to all live observers. Observers that
/// throw in their callbacks are reported via <see cref="System.Diagnostics.Debug"/> and
/// dropped so one bad observer can't poison the others.
/// </summary>
internal sealed class SimpleSubject<T> : IObservable<T>, IObserver<T>, IDisposable
{
    private readonly ConcurrentDictionary<IDisposable, IObserver<T>> _observers = new();
    private bool _completed;
    private Exception? _error;

    public IDisposable Subscribe(IObserver<T> observer)
    {
        if (observer is null) throw new ArgumentNullException(nameof(observer));

        var token = new Unsubscriber(this, observer);
        if (!_observers.TryAdd(token, observer))
            throw new InvalidOperationException("Unexpected duplicate observer registration.");

        // Move-along semantics: if the stream already ended, surface it immediately so
        // late subscribers get OnCompleted/OnError instead of hanging.
        if (_completed)
        {
            if (_error is not null) observer.OnError(_error);
            else observer.OnCompleted();
        }

        return token;
    }

    public void OnNext(T value)
    {
        if (_completed) return;
        foreach (var kv in _observers)
        {
            try { kv.Value.OnNext(value); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SimpleSubject] observer OnNext threw: {ex}"); }
        }
    }

    public void OnError(Exception error)
    {
        if (_completed) return;
        _completed = true;
        _error = error;
        foreach (var kv in _observers)
        {
            try { kv.Value.OnError(error); }
            catch { /* swallow */ }
        }
        _observers.Clear();
    }

    public void OnCompleted()
    {
        if (_completed) return;
        _completed = true;
        foreach (var kv in _observers)
        {
            try { kv.Value.OnCompleted(); }
            catch { /* swallow */ }
        }
        _observers.Clear();
    }

    public void Dispose()
    {
        OnCompleted();
        _observers.Clear();
    }

    private sealed class Unsubscriber : IDisposable
    {
        private readonly SimpleSubject<T> _subject;
        public IObserver<T> Observer { get; }

        public Unsubscriber(SimpleSubject<T> subject, IObserver<T> observer)
        {
            _subject = subject;
            Observer = observer;
        }

        public void Dispose()
        {
            _subject._observers.TryRemove(this, out _);
        }
    }
}