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
        BitConverter.GetBytes(0x41727101980L).CopyTo(request, 0);
        // Action (0 = connect)
        BitConverter.GetBytes(0).CopyTo(request, 8);
        var transactionId = GenerateTransactionId();
        BitConverter.GetBytes(transactionId).CopyTo(request, 12);

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

        var action = BitConverter.ToInt32(buffer, 0);
        var receivedTransactionId = BitConverter.ToInt32(buffer, 4);

        if (receivedTransactionId != transactionId)
            throw new InvalidOperationException($"Transaction ID mismatch: expected {transactionId}, got {receivedTransactionId}");

        if (action == 3) // Error
        {
            var errorMessage = Encoding.ASCII.GetString(buffer, 8, buffer.Length - 8);
            throw new InvalidOperationException($"Tracker connection error: {errorMessage}");
        }

        if (action != 0)
            throw new InvalidOperationException($"Unexpected action in connect response: {action}");

        return BitConverter.ToInt64(buffer, 8);
    }

    private async Task<List<IPEndPoint>> AnnounceToTracker(
        UdpClient udpClient, IPEndPoint endpoint, long connectionId,
        byte[] infoHash, string peerId, int port,
        long downloaded, long uploaded, long left)
    {
        using var cts = new CancellationTokenSource(_timeout);

        var request = new byte[98];
        // connection_id
        BitConverter.GetBytes(connectionId).CopyTo(request, 0);
        // action (1 = announce)
        BitConverter.GetBytes(1).CopyTo(request, 8);
        var transactionId = GenerateTransactionId();
        // transaction_id
        BitConverter.GetBytes(transactionId).CopyTo(request, 12);
        // info_hash (20 bytes)
        infoHash.CopyTo(request, 16);
        // peer_id (20 bytes)
        Encoding.ASCII.GetBytes(peerId).CopyTo(request, 36);
        // downloaded
        BitConverter.GetBytes(downloaded).CopyTo(request, 56);
        // left
        BitConverter.GetBytes(left).CopyTo(request, 64);
        // uploaded
        BitConverter.GetBytes(uploaded).CopyTo(request, 72);
        // event (0 = none)
        BitConverter.GetBytes(0).CopyTo(request, 80);
        // IP address (0 = default)
        BitConverter.GetBytes(0).CopyTo(request, 84);
        // key
        BitConverter.GetBytes(GenerateTransactionId()).CopyTo(request, 88);
        // num_want (-1 = default)
        BitConverter.GetBytes(-1).CopyTo(request, 92);
        // port
        BitConverter.GetBytes((ushort)port).CopyTo(request, 96);

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
}