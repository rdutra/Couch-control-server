namespace CouchControl.Agent.Status;

public sealed record AgentStatusSnapshot(
    string CurrentMode,
    string CurrentOperation,
    string CurrentStep,
    string ConfiguredTv,
    string TvConnectionStatus,
    string ListeningAddresses,
    string SteamStatus,
    string LastResult);
