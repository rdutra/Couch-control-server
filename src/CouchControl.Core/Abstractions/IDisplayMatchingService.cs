using System.Collections.Generic;
using CouchControl.Core.Models;

namespace CouchControl.Core.Abstractions;

public interface IDisplayMatchingService
{
    /// <summary>
    /// Matches the saved couch display identity against the list of currently connected displays.
    /// Tries exact device path, manufacturer/product/serial, adapter/target ID, and friendly name.
    /// Throws an exception if no match is found or if the match is ambiguous.
    /// </summary>
    DisplayDevice MatchDisplay(
        CouchDisplayIdentity target,
        IReadOnlyList<DisplayDevice> connectedDisplays);
}
