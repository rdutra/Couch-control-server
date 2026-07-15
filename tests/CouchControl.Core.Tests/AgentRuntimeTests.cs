using CouchControl.Windows.Runtime;
using CouchControl.Windows.Startup;
using Microsoft.Win32;

namespace CouchControl.Core.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public void StartupRegistration_SetsAndRemovesCurrentUserRunValue()
    {
        var registry = new FakeRegistryValueStore();
        var registration = new CurrentUserStartupRegistration(registry);

        registration.SetEnabled("CouchControl.Agent", "\"C:\\Apps\\CouchControl.Agent.exe\"", enabled: true);

        Assert.True(registration.IsEnabled("CouchControl.Agent"));
        Assert.Equal(
            "\"C:\\Apps\\CouchControl.Agent.exe\"",
            registry.Values[(RegistryHive.CurrentUser, CurrentUserStartupRegistration.RunKeyPath, "CouchControl.Agent")]);

        registration.SetEnabled("CouchControl.Agent", "\"C:\\Apps\\CouchControl.Agent.exe\"", enabled: false);

        Assert.False(registration.IsEnabled("CouchControl.Agent"));
        Assert.Empty(registry.Values);
    }

    [Fact]
    public void StartupRegistration_BuildCommandLine_QuotesExecutableAndArguments()
    {
        var commandLine = CurrentUserStartupRegistration.BuildCommandLine(
            @"C:\Program Files\Couch Control\CouchControl.Agent.exe",
            "--minimized",
            "living room");

        Assert.Equal(
            "\"C:\\Program Files\\Couch Control\\CouchControl.Agent.exe\" \"--minimized\" \"living room\"",
            commandLine);
    }

    [Fact]
    public void SingleInstanceCoordinator_PrimaryInstanceRegistersActivationHandler()
    {
        var mutex = new FakeInstanceMutex(canAcquire: true);
        var signal = new FakeInstanceSignal();
        var coordinator = new SingleInstanceCoordinator(mutex, signal);
        var activationCount = 0;

        coordinator.ActivationRequested += (_, _) => activationCount++;

        Assert.True(coordinator.TryAcquirePrimaryInstance());

        signal.Trigger();

        Assert.Equal(1, activationCount);
    }

    [Fact]
    public void SingleInstanceCoordinator_SecondaryInstanceSignalsExistingPrimary()
    {
        var mutex = new FakeInstanceMutex(canAcquire: false);
        var signal = new FakeInstanceSignal();
        var coordinator = new SingleInstanceCoordinator(mutex, signal);

        Assert.False(coordinator.TryAcquirePrimaryInstance());
        Assert.True(coordinator.NotifyPrimaryInstance());
        Assert.Equal(1, signal.SignalCount);
    }

    [Fact]
    public void SingleInstanceCoordinator_DisposeReleasesPrimaryMutex()
    {
        var mutex = new FakeInstanceMutex(canAcquire: true);
        var signal = new FakeInstanceSignal();
        var coordinator = new SingleInstanceCoordinator(mutex, signal);

        Assert.True(coordinator.TryAcquirePrimaryInstance());

        coordinator.Dispose();

        Assert.True(mutex.ReleaseCalled);
        Assert.True(signal.DisposeCalled);
    }

    private sealed class FakeRegistryValueStore : IRegistryValueStore
    {
        public Dictionary<(RegistryHive Hive, string SubKey, string ValueName), string> Values { get; } = [];

        public string? GetStringValue(RegistryHive hive, string subKey, string valueName) =>
            Values.TryGetValue((hive, subKey, valueName), out var value)
                ? value
                : null;

        public void SetStringValue(RegistryHive hive, string subKey, string valueName, string value) =>
            Values[(hive, subKey, valueName)] = value;

        public void DeleteValue(RegistryHive hive, string subKey, string valueName) =>
            Values.Remove((hive, subKey, valueName));
    }

    private sealed class FakeInstanceMutex(bool canAcquire) : IInstanceMutex
    {
        public bool ReleaseCalled { get; private set; }

        public bool TryAcquire() => canAcquire;

        public void Release() => ReleaseCalled = true;

        public void Dispose()
        {
        }
    }

    private sealed class FakeInstanceSignal : IInstanceSignal
    {
        private Action? callback;

        public int SignalCount { get; private set; }

        public bool DisposeCalled { get; private set; }

        public void Register(Action callback) => this.callback = callback;

        public bool Signal()
        {
            SignalCount++;
            return true;
        }

        public void Trigger() => callback?.Invoke();

        public void Dispose() => DisposeCalled = true;
    }
}
