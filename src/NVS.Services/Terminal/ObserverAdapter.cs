namespace NVS.Services.Terminal;

/// <summary>
/// Adapter that converts callbacks into an <see cref="IObserver{T}"/> for subscribing
/// to <see cref="IProcessTerminal.OutputObservable"/> (or any <see cref="IObservable{T}"/>)
/// without a dependency on System.Reactive.
/// </summary>
public sealed class ObserverAdapter<T> : IObserver<T>
{
    private readonly Action<T> _onNext;
    private readonly Action? _onCompleted;
    private readonly Action<Exception>? _onError;

    public ObserverAdapter(Action<T> onNext, Action? onCompleted = null, Action<Exception>? onError = null)
    {
        _onNext = onNext;
        _onCompleted = onCompleted;
        _onError = onError;
    }

    public void OnNext(T value) => _onNext(value);
    public void OnError(Exception error) => _onError?.Invoke(error);
    public void OnCompleted() => _onCompleted?.Invoke();
}