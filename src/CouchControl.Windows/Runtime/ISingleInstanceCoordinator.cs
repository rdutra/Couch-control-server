namespace CouchControl.Windows.Runtime;

public interface ISingleInstanceCoordinator : IDisposable
{
    event EventHandler? ActivationRequested;

    bool TryAcquirePrimaryInstance();

    bool NotifyPrimaryInstance();
}
