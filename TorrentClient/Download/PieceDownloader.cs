using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TorrentClient.Download;


public class PieceDownloader : IDisposable
{
    public IPEndPoint Peer { get; set; }
    public string PeerId { get; private set; }
    public bool IsUnchoked { get; private set; }
    public bool[] AvailablePieces { get; private set; }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly byte[] _infoHash;
    private readonly int _totalPieces;
    private bool _initialized = false;
    private readonly object _lock = new object();

    public static async Task<PieceDownloader> CreateAsync(byte[] infoHash, string peerId, IPEndPoint peer, int totalPieces)
    {
        var client = new TcpClient();
        await client.ConnectAsync(peer.Address, peer.Port);

        var stream = client.GetStream();
        stream.ReadTimeout = 30000;
        stream.WriteTimeout = 30000;

        bool handshakeSuccess = await PerformHandshakeAsync(stream, infoHash, peerId);
        if (!handshakeSuccess)
            throw new Exception("Handshake failed");

        var downloader = new PieceDownloader(client, stream, infoHash, peerId, peer, totalPieces);

        await downloader.InitializeAsync();

        return downloader;
    }

    private PieceDownloader(TcpClient client, NetworkStream stream, byte[] infoHash, string peerId, IPEndPoint peer, int totalPieces)
    {
        _client = client;
        _stream = stream;
        _infoHash = infoHash;
        PeerId = peerId;
        Peer = peer;
        _totalPieces = totalPieces;
        AvailablePieces = new bool[totalPieces];
    }

    private async Task InitializeAsync()
    {
        if (_initialized) return;

        await ExchangeMessages();
        _initialized = true;

        Console.WriteLine($"[Peer {Peer}] Initialized: Unchoked={IsUnchoked}, AvailablePieces={AvailablePieces.Count(x => x)}");
    }

    public bool HasPiece(int pieceIndex)
    {
        return pieceIndex >= 0 && pieceIndex < AvailablePieces.Length && AvailablePieces[pieceIndex];
    }

    public static async Task<bool> PerformHandshakeAsync(Stream stream, byte[] infoHash, string peerId)
    {
        // Создание и отправка handshake
        var handshake = CreateHandshake(infoHash, peerId);
        await stream.WriteAsync(handshake);

        // Получение ответа
        var response = new byte[68];
        int bytesRead = await stream.ReadAsync(response);

        if (bytesRead != 68)
            throw new Exception($"Handshake response incomplete: {bytesRead} bytes");

        // Проверка совпадения infoHash
        var responseInfoHash = response[28..48];
        return infoHash.SequenceEqual(responseInfoHash);
    }

    public async Task<byte[]> DownloadPieceFromPeer(int pieceIndex, int pieceLength)
    {
        if (!_initialized)
            await InitializeAsync();

        if (!IsUnchoked)
            throw new Exception($"Peer {Peer} is choking us");

        if (!HasPiece(pieceIndex))
            throw new Exception($"Peer {Peer} doesn't have piece {pieceIndex}");

        Console.WriteLine($"[Peer {Peer}] Downloading piece {pieceIndex}...");

        return await DownloadPieceData(pieceIndex, pieceLength);
    }

    private async Task ExchangeMessages()
    {
        Console.WriteLine($"[Peer {Peer}] Starting message exchange...");

        bool gotBitfield = false;
        bool gotUnchoke = false;

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (!gotBitfield || !gotUnchoke)
        {
            if (cts.Token.IsCancellationRequested)
                throw new Exception("Message exchange timeout");

            var message = await ReadMessageAsync(cts.Token);
            if (message == null) continue;

            switch (message.Id)
            {
                case 5: // BitField
                    ParseBitField(message.Data, AvailablePieces);
                    gotBitfield = true;
                    Console.WriteLine($"[Peer {Peer}] Received BitField - {AvailablePieces.Count(x => x)} pieces available");

                    await SendInterested();
                    break;

                case 1: // Unchoke
                    IsUnchoked = true;
                    gotUnchoke = true;
                    Console.WriteLine($"[Peer {Peer}] Received Unchoke - ready to download!");
                    break;

                case 0: // Choke
                    IsUnchoked = false;
                    Console.WriteLine($"[Peer {Peer}] Received Choke - blocked");
                    break;

                case 4: // Have
                    if (message.Data.Length >= 4)
                    {
                        int havePiece = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(message.Data, 0));
                        if (havePiece >= 0 && havePiece < AvailablePieces.Length)
                        {
                            AvailablePieces[havePiece] = true;
                        }
                    }
                    break;

                default:
                    Console.WriteLine($"[Peer {Peer}] Received message ID={message.Id}, Length={message.Data.Length}");
                    break;
            }
        }
    }

    private async Task SendInterested()
    {
        var interested = new byte[] { 0, 0, 0, 1, 2 };
        await _stream.WriteAsync(interested);
        Console.WriteLine($"[Peer {Peer}] Sent Interested");
    }

    private async Task<PeerMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        try
        {
            var lengthBytes = new byte[4];
            await ReadFullAsync(_stream, lengthBytes, 4, ct);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

            if (length == 0)
            {
                return null;
            }

            if (length > 1000000)
                throw new Exception($"Message too large: {length} bytes");

            var data = new byte[length];
            await ReadFullAsync(_stream, data, length, ct);

            byte messageId = data[0];
            var messageData = new byte[length - 1];
            if (length > 1)
            {
                Buffer.BlockCopy(data, 1, messageData, 0, length - 1);
            }

            return new PeerMessage(messageId, messageData);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to read message: {ex.Message}");
        }
    }

    private static void ParseBitField(byte[] bitfieldData, bool[] availablePieces)
    {
        int totalBits = bitfieldData.Length * 8;

        for (int i = 0; i < Math.Min(availablePieces.Length, totalBits); i++)
        {
            int byteIndex = i / 8;
            int bitIndex = 7 - (i % 8);
            availablePieces[i] = (bitfieldData[byteIndex] & (1 << bitIndex)) != 0;
        }
    }

    private async Task<byte[]> DownloadPieceData(int pieceIndex, int pieceLength)
    {
        var pieceData = new byte[pieceLength];
        int bytesDownloaded = 0;
        int blockSize = 16384;

        while (bytesDownloaded < pieceLength)
        {
            int requestSize = Math.Min(blockSize, pieceLength - bytesDownloaded);

            var request = CreateRequestMessage(pieceIndex, bytesDownloaded, requestSize);
            await _stream.WriteAsync(request);

            var pieceMessage = await ReadPieceMessage();
            if (pieceMessage != null && pieceMessage.Index == pieceIndex && pieceMessage.Begin == bytesDownloaded)
            {
                int copyLength = Math.Min(pieceMessage.Data.Length, pieceLength - bytesDownloaded);
                Buffer.BlockCopy(pieceMessage.Data, 0, pieceData, bytesDownloaded, copyLength);
                bytesDownloaded += copyLength;

                if (bytesDownloaded % (blockSize * 10) == 0)
                {
                    Console.WriteLine($"[Peer {Peer}] Piece {pieceIndex}: {bytesDownloaded}/{pieceLength} bytes");
                }
            }
            else
            {
                throw new Exception($"Invalid piece message received");
            }
        }

        Console.WriteLine($"[Peer {Peer}] Piece {pieceIndex} completed: {pieceLength} bytes");
        return pieceData;
    }

    private async Task<PieceMessage?> ReadPieceMessage()
    {
        while (true)
        {
            var message = await ReadMessageAsync();
            if (message == null) continue;

            if (message.Id == 7)
            {
                if (message.Data.Length >= 8)
                {
                    int index = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(message.Data, 0));
                    int begin = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(message.Data, 4));
                    var data = new byte[message.Data.Length - 8];
                    Buffer.BlockCopy(message.Data, 8, data, 0, data.Length);

                    return new PieceMessage(index, begin, data);
                }
            }
            else if (message.Id == 0)
            {
                IsUnchoked = false;
                throw new Exception("Peer choked us during download");
            }
            else
            {
                Console.WriteLine($"[Peer {Peer}] Unexpected message during download: ID={message.Id}");
            }
        }
    }

    private static async Task ReadFullAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken ct = default)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) throw new Exception("Connection closed");
            totalRead += read;
        }
    }

    private static byte[] CreateHandshake(byte[] infoHash, string peerId)
    {
        var handshake = new byte[68];
        handshake[0] = 19;
        Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(handshake, 1);
        new byte[8].CopyTo(handshake, 20);
        infoHash.CopyTo(handshake, 28);
        Encoding.ASCII.GetBytes(peerId).CopyTo(handshake, 48);
        return handshake;
    }

    private static byte[] CreateRequestMessage(int pieceIndex, int begin, int length)
    {
        var message = new byte[17];
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(13)).CopyTo(message, 0);
        message[4] = 6;
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(pieceIndex)).CopyTo(message, 5);
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(begin)).CopyTo(message, 9);
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(length)).CopyTo(message, 13);
        return message;
    }

    public void Dispose()
    {
        _stream?.Close();
        _client?.Close();
    }
}

public record PeerMessage(byte Id, byte[] Data);
public record PieceMessage(int Index, int Begin, byte[] Data);
