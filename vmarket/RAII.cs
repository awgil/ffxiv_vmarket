namespace Market;

// usage: using var x = new RAII(action);
public readonly record struct RAII(Action a) : IDisposable
{
    public void Dispose() => a();
}
