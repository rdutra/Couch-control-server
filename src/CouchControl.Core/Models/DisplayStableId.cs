using System;
using System.Security.Cryptography;
using System.Text;

namespace CouchControl.Core.Models;

public static class DisplayStableId
{
    public static string FromDevicePath(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath))
        {
            return "unknown";
        }

        using var sha256 = SHA256.Create();
        byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(devicePath.Trim()));
        return Convert.ToHexString(hash[..6]).ToLowerInvariant();
    }
}
