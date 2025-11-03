using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TorrentClient.Tracker;

public class UdpTrackerClient : ITrackerClient
{
    private readonly string _announce;
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    public UdpTrackerClient(string announceUrl)
    {
        _announce = announceUrl;
    }

    public async Task<List<IPEndPoint>> AnnounceAsync(
        byte[] infoHash, string peerId, int port,
        long downloaded, long uploaded, long left, bool compact)
    {
        try
        {
            var uri = new Uri(_announce);
            var host = uri.Host;
            var trackerPort = uri.Port > 0 ? uri.Port : 6969;

            Console.WriteLine($"[UDP] Connecting to {host}:{trackerPort}");

            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
                throw new InvalidOperationException($"Cannot resolve host: {host}");

            var endpoint = new IPEndPoint(addresses[0], trackerPort);

            using var udpClient = new UdpClient();
            udpClient.Client.ReceiveTimeout = (int)_timeout.TotalMilliseconds;

            var connectionId = await ConnectToTracker(udpClient, endpoint);
            Console.WriteLine($"[UDP] Connected, connectionId: {connectionId}");

            var peers = await AnnounceToTracker(udpClient, endpoint, connectionId,
                infoHash, peerId, port, downloaded, uploaded, left);

            Console.WriteLine($"[UDP] Announce successful, found {peers.Count} peers");
            return peers;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UDP] Error: {ex.Message}");
            return new List<IPEndPoint>();
        }
    }

    private async Task<long> ConnectToTracker(UdpClient udpClient, IPEndPoint endpoint)
    {
        using var cts = new CancellationTokenSource(_timeout);

        var request = new byte[16];
        // Protocol ID (0x41727101980)
        WriteInt64BE(request.AsSpan(0, 8), 0x0000041727101980L);
        // Action (0 = connect)
        WriteInt32BE(request.AsSpan(8, 4), 0);
        var transactionId = GenerateTransactionId();
        WriteInt32BE(request.AsSpan(12, 4), transactionId);

        Console.WriteLine($"[UDP] Sending connect request, transaction: {transactionId}");

        await udpClient.SendAsync(request, request.Length, endpoint);

        UdpReceiveResult response;
        try
        {
            response = await udpClient.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Connect request timeout");
        }

        var buffer = response.Buffer;
        Console.WriteLine($"[UDP] Connect response: {buffer.Length} bytes");

        if (buffer.Length < 16)
            throw new InvalidOperationException("Invalid connect response length");


        var action = ReadInt32BE(buffer.AsSpan(0, 4));
        var receivedTransactionId = ReadInt32BE(buffer.AsSpan(4, 4));

        if (receivedTransactionId != transactionId)
            throw new InvalidOperationException($"Transaction ID mismatch: expected {transactionId}, got {receivedTransactionId}");

        if (action == 3) // Error
        {
            var errorMessage = Encoding.ASCII.GetString(buffer, 8, buffer.Length - 8);
            throw new InvalidOperationException($"Tracker connection error: {errorMessage}");
        }

        if (action != 0)
            throw new InvalidOperationException($"Unexpected action in connect response: {action}");

        return ReadInt64BE(buffer.AsSpan(8, 8));
    }

    private async Task<List<IPEndPoint>> AnnounceToTracker(
        UdpClient udpClient, IPEndPoint endpoint, long connectionId,
        byte[] infoHash, string peerId, int port,
        long downloaded, long uploaded, long left)
    {
        using var cts = new CancellationTokenSource(_timeout);

        var request = new byte[98];
        // connection_id
        WriteInt64BE(request.AsSpan(0, 8), connectionId);
        // action (1 = announce)
        WriteInt32BE(request.AsSpan(8, 4), 1);
        var transactionId = GenerateTransactionId();
        // transaction_id
        WriteInt32BE(request.AsSpan(12, 4), transactionId);
        // info_hash (20 bytes)
        infoHash.CopyTo(request, 16);
        // peer_id (20 bytes)
        Encoding.ASCII.GetBytes(peerId).CopyTo(request, 36);
        // downloaded
        WriteInt64BE(request.AsSpan(56, 8), downloaded);
        // left
        WriteInt64BE(request.AsSpan(64, 8), left);
        // uploaded
        WriteInt64BE(request.AsSpan(72, 8), uploaded);
        // event (0 = none)
        WriteInt32BE(request.AsSpan(80, 4), 0);
        // IP address (0 = default)
        WriteInt32BE(request.AsSpan(84, 4), 0);
        // key
        WriteInt32BE(request.AsSpan(88, 4), GenerateTransactionId());
        // num_want (-1 = default)
        WriteInt32BE(request.AsSpan(92, 4), -1);
        // port
        WriteInt32BE(request.AsSpan(96, 4), (ushort)port);

        Console.WriteLine($"[UDP] Sending announce request, transaction: {transactionId}");

        await udpClient.SendAsync(request, request.Length, endpoint);

        UdpReceiveResult response;
        try
        {
            response = await udpClient.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("Announce request timeout");
        }

        var buffer = response.Buffer;
        Console.WriteLine($"[UDP] Announce response: {buffer.Length} bytes");

        if (buffer.Length < 20)
            throw new InvalidOperationException("Invalid announce response length");

        var action = BitConverter.ToInt32(buffer, 0);
        var receivedTransactionId = BitConverter.ToInt32(buffer, 4);

        if (receivedTransactionId != transactionId)
            throw new InvalidOperationException($"Transaction ID mismatch: expected {transactionId}, got {receivedTransactionId}");

        if (action == 3) // Error
        {
            var errorMessage = Encoding.ASCII.GetString(buffer, 8, buffer.Length - 8);
            throw new InvalidOperationException($"Tracker announce error: {errorMessage}");
        }

        if (action != 1)
            throw new InvalidOperationException($"Unexpected action in announce response: {action}");

        var peers = ParseCompactPeers(buffer, 20);
        return peers;
    }

    private static List<IPEndPoint> ParseCompactPeers(byte[] data, int startIndex)
    {
        var peers = new List<IPEndPoint>();
        var peerDataLength = data.Length - startIndex;

        if (peerDataLength % 6 != 0)
        {
            Console.WriteLine($"[UDP] Warning: Peer data length {peerDataLength} is not divisible by 6");
        }

        for (int i = startIndex; i + 6 <= data.Length; i += 6)
        {
            try
            {
                var ipBytes = new byte[4];
                Array.Copy(data, i, ipBytes, 0, 4);
                var ip = new IPAddress(ipBytes);

                var peerPort = (ushort)((data[i + 4] << 8) | data[i + 5]);

                // Фильтруем некорректные порты
                if (peerPort < 1024 || peerPort > 65535)
                {
                    Console.WriteLine($"[UDP] Skipping peer with invalid port: {ip}:{peerPort}");
                    continue;
                }

                var peer = new IPEndPoint(ip, peerPort);
                peers.Add(peer);
                Console.WriteLine($"[UDP] Found peer: {peer}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UDP] Error parsing peer at offset {i}: {ex.Message}");
            }
        }

        return peers;
    }

    private static int GenerateTransactionId()
    {
        return new Random().Next(1, int.MaxValue);
    }

    private static void WriteInt32BE(Span<byte> dst, int value)
    {
        dst[0] = (byte)((value >> 24) & 0xFF);
        dst[1] = (byte)((value >> 16) & 0xFF);
        dst[2] = (byte)((value >> 8) & 0xFF);
        dst[3] = (byte)(value & 0xFF);
    }
    private static void WriteInt64BE(Span<byte> dst, long value)
    {
        for (int i = 7; i >= 0; i--) { dst[7 - i] = (byte)((value >> (i * 8)) & 0xFF); }
    }
    private static void WriteUInt16BE(Span<byte> dst, ushort value)
    {
        dst[0] = (byte)((value >> 8) & 0xFF);
        dst[1] = (byte)(value & 0xFF);
    }
    private static int ReadInt32BE(ReadOnlySpan<byte> src)
    {
        return (src[0] << 24) | (src[1] << 16) | (src[2] << 8) | src[3];
    }
    private static long ReadInt64BE(ReadOnlySpan<byte> src)
    {
        long v = 0;
        for (int i = 0; i < 8; i++) v = (v << 8) | src[i];
        return v;
    }
}