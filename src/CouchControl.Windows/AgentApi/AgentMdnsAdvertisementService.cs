using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using CouchControl.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CouchControl.Windows.AgentApi;

public interface IAgentMdnsAdvertisementService : IAsyncDisposable
{
    Task StartAsync(AgentApiBindingPlan bindingPlan, CancellationToken cancellationToken = default);
}

public sealed class AgentMdnsAdvertisementService : IAgentMdnsAdvertisementService
{
    internal const string ServiceType = "_couchcontrol._tcp.local.";

    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint MulticastEndpoint = new(MulticastAddress, 5353);

    private readonly IAgentConfigurationStore configurationStore;
    private readonly ILogger<AgentMdnsAdvertisementService> logger;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private readonly object lifecycleLock = new();

    private UdpClient? client;
    private Task? receiveTask;
    private MdnsAdvertisement? advertisement;

    public AgentMdnsAdvertisementService(
        IAgentConfigurationStore configurationStore,
        ILogger<AgentMdnsAdvertisementService> logger)
    {
        this.configurationStore = configurationStore;
        this.logger = logger;
    }

    public async Task StartAsync(AgentApiBindingPlan bindingPlan, CancellationToken cancellationToken = default)
    {
        if (bindingPlan.LanIpv4Addresses.Count == 0)
        {
            logger.LogInformation("Skipping mDNS advertisement because no LAN IPv4 address is available.");
            return;
        }

        lock (lifecycleLock)
        {
            if (client is not null)
            {
                return;
            }
        }

        var configuration = await configurationStore.LoadAsync(cancellationToken);
        var hostName = SanitizeLabel(Dns.GetHostName(), "couchctrl-pc");
        var displayName = DisplayNameForAdvertisement(configuration.AgentName, hostName);
        var instanceName = SanitizeLabel(displayName, "CouchCTRL PC");
        var selectedAddress = bindingPlan.LanIpv4Addresses
            .Select(static address => IPAddress.TryParse(address, out var parsed) ? parsed : null)
            .OfType<IPAddress>()
            .FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork);
        if (selectedAddress is null)
        {
            logger.LogInformation("Skipping mDNS advertisement because the selected LAN address is invalid.");
            return;
        }

        UdpClient localClient;
        try
        {
            localClient = CreateClient();
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            logger.LogWarning(ex, "Skipping mDNS advertisement because the mDNS socket could not be opened.");
            return;
        }

        var localAdvertisement = new MdnsAdvertisement(
            $"{instanceName}.{ServiceType}",
            $"{hostName}.local.",
            bindingPlan.Port,
            selectedAddress,
            [
                "api=/api/v1",
                "version=1",
                $"name={displayName}"
            ]);

        lock (lifecycleLock)
        {
            if (client is not null)
            {
                localClient.Dispose();
                return;
            }

            client = localClient;
            advertisement = localAdvertisement;
            receiveTask = Task.Run(() => ReceiveLoopAsync(localClient, cancellationTokenSource.Token));
        }

        try
        {
            await SendAnnouncementAsync(localClient, localAdvertisement, cancellationToken);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            logger.LogDebug(ex, "Initial mDNS announcement could not be sent.");
        }

        logger.LogInformation(
            "Advertising CouchCTRL agent over mDNS as {InstanceName} at {Address}:{Port}.",
            localAdvertisement.InstanceName,
            localAdvertisement.Address,
            localAdvertisement.Port);
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        UdpClient? localClient;
        Task? localTask;
        lock (lifecycleLock)
        {
            localClient = client;
            localTask = receiveTask;
            client = null;
            receiveTask = null;
            advertisement = null;
        }

        localClient?.Dispose();
        if (localTask is not null)
        {
            try
            {
                await localTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        cancellationTokenSource.Dispose();
    }

    private static UdpClient CreateClient()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = false
        };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        socket.Bind(new IPEndPoint(IPAddress.Any, 5353));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastAddress));
        return new UdpClient { Client = socket };
    }

    private async Task ReceiveLoopAsync(UdpClient udpClient, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udpClient.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "mDNS receive loop stopped after socket error.");
                break;
            }

            MdnsAdvertisement? currentAdvertisement;
            lock (lifecycleLock)
            {
                currentAdvertisement = advertisement;
            }

            if (currentAdvertisement is null ||
                !MdnsPacketBuilder.QueryMatches(received.Buffer, currentAdvertisement))
            {
                continue;
            }

            try
            {
                var response = MdnsPacketBuilder.BuildResponse(currentAdvertisement, transactionId: 0);
                await udpClient.SendAsync(response, MulticastEndpoint, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
            {
                logger.LogDebug(ex, "Failed to send mDNS response.");
            }
        }
    }

    private static async Task SendAnnouncementAsync(
        UdpClient udpClient,
        MdnsAdvertisement advertisement,
        CancellationToken cancellationToken)
    {
        var response = MdnsPacketBuilder.BuildResponse(advertisement, transactionId: 0);
        await udpClient.SendAsync(response, MulticastEndpoint, cancellationToken);
    }

    private static string SanitizeLabel(string? value, string fallback)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character) || character is '-' or ' ')
            {
                builder.Append(character);
            }
        }

        var label = builder.ToString().Trim();
        return label.Length == 0 ? fallback : label[..Math.Min(label.Length, 63)];
    }

    private static string DisplayNameForAdvertisement(string agentName, string hostName) =>
        string.IsNullOrWhiteSpace(agentName) ||
        string.Equals(agentName, "CouchControl Agent", StringComparison.OrdinalIgnoreCase)
            ? hostName
            : agentName;
}

internal sealed record MdnsAdvertisement(
    string InstanceName,
    string HostName,
    int Port,
    IPAddress Address,
    IReadOnlyList<string> TxtRecords);

internal static class MdnsPacketBuilder
{
    private const ushort PtrRecordType = 12;
    private const ushort TxtRecordType = 16;
    private const ushort SrvRecordType = 33;
    private const ushort ARecordType = 1;
    private const ushort InternetClass = 1;
    private const uint TtlSeconds = 120;

    public static bool QueryMatches(byte[] packet, MdnsAdvertisement advertisement)
    {
        if (packet.Length < 12)
        {
            return false;
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4, 2));
        var offset = 12;
        for (var index = 0; index < questionCount; index++)
        {
            if (!TryReadName(packet, ref offset, out var name) || offset + 4 > packet.Length)
            {
                return false;
            }

            var recordType = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset, 2));
            offset += 4;
            if (IsRelevantQuestion(name, recordType, advertisement))
            {
                return true;
            }
        }

        return false;
    }

    public static byte[] BuildResponse(MdnsAdvertisement advertisement, ushort transactionId)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, transactionId);
        WriteUInt16(stream, 0x8400);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 4);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);

        WriteRecord(stream, AgentMdnsAdvertisementService.ServiceType, PtrRecordType, BuildName(advertisement.InstanceName));
        WriteRecord(stream, advertisement.InstanceName, SrvRecordType, BuildSrvRecord(advertisement));
        WriteRecord(stream, advertisement.InstanceName, TxtRecordType, BuildTxtRecord(advertisement.TxtRecords));
        WriteRecord(stream, advertisement.HostName, ARecordType, advertisement.Address.GetAddressBytes());

        return stream.ToArray();
    }

    private static bool IsRelevantQuestion(string name, ushort recordType, MdnsAdvertisement advertisement) =>
        (string.Equals(name, AgentMdnsAdvertisementService.ServiceType, StringComparison.OrdinalIgnoreCase) &&
            recordType is PtrRecordType or 255) ||
        (string.Equals(name, advertisement.InstanceName, StringComparison.OrdinalIgnoreCase) &&
            recordType is SrvRecordType or TxtRecordType or 255) ||
        (string.Equals(name, advertisement.HostName, StringComparison.OrdinalIgnoreCase) &&
            recordType is ARecordType or 255);

    private static bool TryReadName(byte[] packet, ref int offset, out string name)
    {
        var labels = new List<string>();
        var jumps = 0;
        var currentOffset = offset;
        var consumedOffset = -1;

        while (currentOffset < packet.Length)
        {
            var length = packet[currentOffset++];
            if (length == 0)
            {
                offset = consumedOffset >= 0 ? consumedOffset : currentOffset;
                name = string.Join('.', labels) + ".";
                return true;
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (currentOffset >= packet.Length || jumps++ > 8)
                {
                    break;
                }

                consumedOffset = consumedOffset < 0 ? currentOffset + 1 : consumedOffset;
                currentOffset = ((length & 0x3F) << 8) | packet[currentOffset++];
                continue;
            }

            if ((length & 0xC0) != 0 || currentOffset + length > packet.Length)
            {
                break;
            }

            labels.Add(Encoding.UTF8.GetString(packet, currentOffset, length));
            currentOffset += length;
        }

        name = string.Empty;
        return false;
    }

    private static byte[] BuildSrvRecord(MdnsAdvertisement advertisement)
    {
        using var stream = new MemoryStream();
        WriteUInt16(stream, 0);
        WriteUInt16(stream, 0);
        WriteUInt16(stream, (ushort)advertisement.Port);
        WriteName(stream, advertisement.HostName);
        return stream.ToArray();
    }

    private static byte[] BuildTxtRecord(IReadOnlyList<string> records)
    {
        using var stream = new MemoryStream();
        foreach (var record in records)
        {
            var bytes = Encoding.UTF8.GetBytes(record);
            stream.WriteByte((byte)Math.Min(bytes.Length, 255));
            stream.Write(bytes, 0, Math.Min(bytes.Length, 255));
        }

        return stream.ToArray();
    }

    private static byte[] BuildName(string name)
    {
        using var stream = new MemoryStream();
        WriteName(stream, name);
        return stream.ToArray();
    }

    private static void WriteRecord(MemoryStream stream, string name, ushort type, byte[] data)
    {
        WriteName(stream, name);
        WriteUInt16(stream, type);
        WriteUInt16(stream, InternetClass | 0x8000);
        WriteUInt32(stream, TtlSeconds);
        WriteUInt16(stream, (ushort)data.Length);
        stream.Write(data, 0, data.Length);
    }

    private static void WriteName(MemoryStream stream, string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte((byte)Math.Min(bytes.Length, 63));
            stream.Write(bytes, 0, Math.Min(bytes.Length, 63));
        }

        stream.WriteByte(0);
    }

    private static void WriteUInt16(MemoryStream stream, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteUInt32(MemoryStream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
