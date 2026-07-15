using System.Threading;

namespace CouchControl.Windows.Runtime;

public sealed class SingleInstanceCoordinator : ISingleInstanceCoordinator
{
    private readonly IInstanceMutex instanceMutex;
    private readonly IInstanceSignal instanceSignal;
    private bool isPrimaryInstance;
    private bool isDisposed;

    public SingleInstanceCoordinator(string applicationId)
        : this(
            new NamedInstanceMutex($@"Local\{applicationId}.Mutex"),
            new NamedInstanceSignal($@"Local\{applicationId}.Activate"))
    {
    }

    internal SingleInstanceCoordinator(
        IInstanceMutex instanceMutex,
        IInstanceSignal instanceSignal)
    {
        this.instanceMutex = instanceMutex;
        this.instanceSignal = instanceSignal;
    }

    public event EventHandler? ActivationRequested;

    public bool TryAcquirePrimaryInstance()
    {
        ThrowIfDisposed();

        if (isPrimaryInstance)
        {
            return true;
        }

        if (!instanceMutex.TryAcquire())
        {
            return false;
        }

        instanceSignal.Register(OnActivationRequested);
        isPrimaryInstance = true;
        return true;
    }

    public bool NotifyPrimaryInstance()
    {
        ThrowIfDisposed();
        return instanceSignal.Signal();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        instanceSignal.Dispose();

        if (isPrimaryInstance)
        {
            instanceMutex.Release();
        }

        instanceMutex.Dispose();
    }

    private void OnActivationRequested() => ActivationRequested?.Invoke(this, EventArgs.Empty);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
    }
}

internal interface IInstanceMutex : IDisposable
{
    bool TryAcquire();

    void Release();
}

internal sealed class NamedInstanceMutex : IInstanceMutex
{
    private readonly Mutex mutex;
    private readonly bool ownsMutex;
    private bool released;

    public NamedInstanceMutex(string name)
    {
        mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        ownsMutex = createdNew;
    }

    public bool TryAcquire() => ownsMutex;

    public void Release()
    {
        if (!ownsMutex || released)
        {
            return;
        }

        mutex.ReleaseMutex();
        released = true;
    }

    public void Dispose() => mutex.Dispose();
}

internal interface IInstanceSignal : IDisposable
{
    void Register(Action callback);

    bool Signal();
}

internal sealed class NamedInstanceSignal : IInstanceSignal
{
    private readonly EventWaitHandle waitHandle;
    private RegisteredWaitHandle? registeredWaitHandle;

    public NamedInstanceSignal(string name)
    {
        waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, name);
    }

    public void Register(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        registeredWaitHandle = ThreadPool.RegisterWaitForSingleObject(
            waitHandle,
            static (state, _) => ((Action)state!).Invoke(),
            callback,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public bool Signal()
    {
        try
        {
            return waitHandle.Set();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        registeredWaitHandle?.Unregister(null);
        waitHandle.Dispose();
    }
}
