namespace NVS.Services.Tests;

/// <summary>
/// Adapts lambdas into <see cref="IObserver{T}"/> so test code can subscribe to
/// <see cref="System.IObservable{T}"/> without a System.Reactive dependency.
/// </summary>
internal sealed class AnonymousObserver<T> : IObserver<T>
{
    private readonly Action<T> _onNext;
    private readonly Action<Exception>? _onError;
    private readonly Action? _onCompleted;

    public AnonymousObserver(Action<T> onNext, Action? onCompleted = null, Action<Exception>? onError = null)
    {
        _onNext = onNext;
        _onCompleted = onCompleted;
        _onError = onError;
    }

    public void OnNext(T value) => _onNext(value);
    public void OnError(Exception error) => _onError?.Invoke(error);
    public void OnCompleted() => _onCompleted?.Invoke();
}