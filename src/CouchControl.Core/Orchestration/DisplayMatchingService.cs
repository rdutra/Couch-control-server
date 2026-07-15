using System;
using System.Collections.Generic;
using System.Linq;
using CouchControl.Core.Abstractions;
using CouchControl.Core.Models;

namespace CouchControl.Core.Orchestration;

public sealed class DisplayMatchingService : IDisplayMatchingService
{
    public DisplayDevice MatchDisplay(
        CouchDisplayIdentity target,
        IReadOnlyList<DisplayDevice> connectedDisplays)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (connectedDisplays == null) throw new ArgumentNullException(connectedDisplays == null ? "connectedDisplays" : null);

        // 1. Try Exact monitor device path
        var pathMatches = connectedDisplays
            .Where(d => !string.IsNullOrEmpty(d.DevicePath) &&
                        string.Equals(d.DevicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pathMatches.Count == 1) return pathMatches[0];
        if (pathMatches.Count > 1)
        {
            throw new InvalidOperationException($"Ambiguous match: Multiple displays found with exact device path '{target.DevicePath}'.");
        }

        // 2. Try Manufacturer/product/serial combination when available
        if (!string.IsNullOrEmpty(target.Manufacturer) &&
            !string.IsNullOrEmpty(target.ProductCode) &&
            !string.IsNullOrEmpty(target.SerialOrInstance))
        {
            var comboMatches = connectedDisplays
                .Where(d =>
                {
                    var parsed = ParseDevicePath(d.DevicePath);
                    return parsed != null &&
                           string.Equals(parsed.Value.Manufacturer, target.Manufacturer, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(parsed.Value.ProductCode, target.ProductCode, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(parsed.Value.SerialOrInstance, target.SerialOrInstance, StringComparison.OrdinalIgnoreCase);
                })
                .ToList();

            if (comboMatches.Count == 1) return comboMatches[0];
            if (comboMatches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous match: Multiple displays found with manufacturer '{target.Manufacturer}', product code '{target.ProductCode}', and serial/instance '{target.SerialOrInstance}'.");
            }
        }

        // 3. Try Adapter and target identifiers as a weaker fallback
        if (!string.IsNullOrEmpty(target.AdapterLuid))
        {
            var adapterTargetMatches = connectedDisplays
                .Where(d => string.Equals(d.AdapterLuid, target.AdapterLuid, StringComparison.OrdinalIgnoreCase) &&
                            d.TargetId == target.TargetId)
                .ToList();

            if (adapterTargetMatches.Count == 1) return adapterTargetMatches[0];
            if (adapterTargetMatches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous match: Multiple displays found with adapter LUID '{target.AdapterLuid}' and target ID '{target.TargetId}'.");
            }
        }

        // 4. Try Friendly name only as a final ambiguous fallback
        if (!string.IsNullOrEmpty(target.FriendlyName))
        {
            var friendlyMatches = connectedDisplays
                .Where(d => string.Equals(d.FriendlyName, target.FriendlyName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (friendlyMatches.Count == 1) return friendlyMatches[0];
            if (friendlyMatches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Ambiguous match: Multiple displays found with friendly name '{target.FriendlyName}'.");
            }
        }

        throw new InvalidOperationException(
            $"Display not found. Could not match TV display (Name: '{target.FriendlyName}', Path: '{target.DevicePath}') using any matching strategies.");
    }

    public static (string Manufacturer, string ProductCode, string SerialOrInstance)? ParseDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return null;

        var parts = devicePath.Split('#', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;

        string hardwareId = parts[1];
        string instanceId = parts[2];

        int guidIdx = instanceId.IndexOf('{');
        if (guidIdx > 0)
        {
            instanceId = instanceId.Substring(0, guidIdx).TrimEnd('#');
        }

        if (hardwareId.Length >= 3)
        {
            string manufacturer = hardwareId.Substring(0, 3);
            string productCode = hardwareId.Substring(3);
            return (manufacturer, productCode, instanceId);
        }

        return (hardwareId, "", instanceId);
    }
}
