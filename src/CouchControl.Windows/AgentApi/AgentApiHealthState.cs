namespace CouchControl.Windows.AgentApi;

public interface IAgentApiHealthState
{
    AgentApiHealthSnapshot GetSnapshot();

    void MarkListening(IReadOnlyList<string> listenUrls);

    void MarkNotListening(string reason);
}

public sealed class AgentApiHealthState : IAgentApiHealthState
{
    private readonly object sync = new();
    private AgentApiHealthSnapshot snapshot = new(false, "API is not listening.", Array.Empty<string>());

    public AgentApiHealthSnapshot GetSnapshot()
    {
        lock (sync)
        {
            return snapshot;
        }
    }

    public void MarkListening(IReadOnlyList<string> listenUrls)
    {
        var urls = listenUrls
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var status = urls.Length == 0
            ? "API is listening."
            : $"Listening on {string.Join(", ", urls)}";

        lock (sync)
        {
            snapshot = new AgentApiHealthSnapshot(true, status, urls);
        }
    }

    public void MarkNotListening(string reason)
    {
        lock (sync)
        {
            snapshot = new AgentApiHealthSnapshot(
                false,
                string.IsNullOrWhiteSpace(reason) ? "API is not listening." : reason,
                Array.Empty<string>());
        }
    }
}
