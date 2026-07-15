namespace CouchControl.Windows.Startup;

public interface IStartupRegistration
{
    bool IsEnabled(string applicationName);

    void SetEnabled(string applicationName, string commandLine, bool enabled);
}
