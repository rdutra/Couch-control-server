using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CouchControl.Windows.Persistence;

internal static class AtomicJsonFile
{
    public static async Task<T?> ReadAsync<T>(
        string filePath,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            return await JsonSerializer.DeserializeAsync<T>(stream, options, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to read JSON from '{filePath}': the file is corrupted or invalid JSON. {ex.Message}",
                ex);
        }
    }

    public static async Task WriteAsync<T>(
        string filePath,
        T value,
        JsonSerializerOptions options,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException($"Cannot determine the directory for '{filePath}'.");
        }

        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(
            directory,
            $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tempPath, filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }
}
