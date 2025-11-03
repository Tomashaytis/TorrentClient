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

        // Инициализируем соединение один раз
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

        // Загрузка данных куска
        return await DownloadPieceData(pieceIndex, pieceLength);
    }

    private async Task ExchangeMessages()
    {
        Console.WriteLine($"[Peer {Peer}] Starting message exchange...");

        // Читаем сообщения пока не получим bitfield и unchoke
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

                    // Отправляем Interested после получения BitField
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
        var interested = new byte[] { 0, 0, 0, 1, 2 }; // length=1, id=2
        await _stream.WriteAsync(interested);
        Console.WriteLine($"[Peer {Peer}] Sent Interested");
    }

    private async Task<PeerMessage?> ReadMessageAsync(CancellationToken ct = default)
    {
        try
        {
            // Читаем длину сообщения
            var lengthBytes = new byte[4];
            await ReadFullAsync(_stream, lengthBytes, 4, ct);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

            if (length == 0)
            {
                // Keep-alive
                return null;
            }

            if (length > 1000000)
                throw new Exception($"Message too large: {length} bytes");

            // Читаем данные сообщения
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
            int bitIndex = 7 - (i % 8); // BitTorrent использует big-endian биты
            availablePieces[i] = (bitfieldData[byteIndex] & (1 << bitIndex)) != 0;
        }
    }

    private async Task<byte[]> DownloadPieceData(int pieceIndex, int pieceLength)
    {
        var pieceData = new byte[pieceLength];
        int bytesDownloaded = 0;
        int blockSize = 16384; // 16KB blocks

        while (bytesDownloaded < pieceLength)
        {
            int requestSize = Math.Min(blockSize, pieceLength - bytesDownloaded);

            // Отправляем Request сообщение
            var request = CreateRequestMessage(pieceIndex, bytesDownloaded, requestSize);
            await _stream.WriteAsync(request);

            // Читаем Piece сообщение
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

            if (message.Id == 7) // Piece message
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
            else if (message.Id == 0) // Choke
            {
                IsUnchoked = false;
                throw new Exception("Peer choked us during download");
            }
            else
            {
                // Обрабатываем другие сообщения
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

// Вспомогательные классы
public record PeerMessage(byte Id, byte[] Data);
public record PieceMessage(int Index, int Begin, byte[] Data);

/*using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TorrentClient.Download;

public class PieceDownloader : IDisposable
{
    public IPEndPoint Peer { get; set; }
    public string PeerId { get; private set; }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly byte[] _infoHash;

    public static async Task<PieceDownloader> CreateAsync(byte[] infoHash, string peerId, IPEndPoint peer)
    {
        var client = new TcpClient();
        await client.ConnectAsync(peer.Address, peer.Port);

        var stream = client.GetStream();
        stream.ReadTimeout = 10000;
        stream.WriteTimeout = 10000;

        bool handshakeSuccess = await PerformHandshakeAsync(stream, infoHash, peerId);
        if (!handshakeSuccess)
            throw new Exception("Handshake failed");

        return new PieceDownloader(client, stream, infoHash, peerId, peer);
    }

    private PieceDownloader(TcpClient client, NetworkStream stream, byte[] infoHash, string peerId, IPEndPoint peer)
    {
        _client = client;
        _stream = stream;
        _infoHash = infoHash;
        PeerId = peerId;
        Peer = peer;
    }

    public static async Task<bool> PerformHandshakeAsync(Stream stream, byte[] infoHash, string peerId)
    {
        // Создание и отправка handshake
        var handshake = CreateHandshake(infoHash, peerId);
        await stream.WriteAsync(handshake);

        // Получение ответа
        var response = new byte[68];
        await stream.ReadAsync(response);

        // Проверка совпадения infoHash
        var responseInfoHash = response[28..48];
        return infoHash.SequenceEqual(responseInfoHash);
    }

    public async Task<byte[]> DownloadPieceFromPeer(int pieceIndex, int pieceLength, int piecesCount)
    {
        var availablePieces = new bool[piecesCount];
        // Обмен сообщениями
        await ExchangeMessages(_stream, availablePieces);

        // Загрузка данных куска
        return await DownloadPieceData(_stream, pieceIndex, pieceLength);
    }

    private static async Task ExchangeMessages(NetworkStream stream, bool[] availablePieces)
    {
        // Читаем BitField - какие куски есть у пира
        var lengthBytes = new byte[4];
        await ReadFullAsync(stream, lengthBytes, 4);
        int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

        if (length > 0)
        {
            var message = new byte[length];
            await ReadFullAsync(stream, message, length);

            // АНАЛИЗИРУЕМ сообщение - это BitField?
            if (message.Length > 0 && message[0] == 5) // ID=5 - BitField
            {
                Console.WriteLine("[Protocol] Received BitField message");
                ParseBitField(message, availablePieces);
            }
            else
            {
                Console.WriteLine($"[Protocol] Received non-BitField message: ID={message[0]}");
            }

            // Отправляем Interested - мы хотим скачивать кусок
            var interested = new byte[] { 0, 0, 0, 1, 2 };
            await stream.WriteAsync(interested);
            Console.WriteLine("[Protocol] Sent Interested message");

            // Ждём Unchoke - разрешение на скачивание
            await ReadUnchoke(stream);
        }
    }

    private static void ParseBitField(byte[] message, bool[] availablePieces)
    {
        if (message.Length < 2) return; // Должен быть ID + данные

        // message[0] = ID (5 для BitField)
        byte[] bitfieldData = new byte[message.Length - 1];
        Array.Copy(message, 1, bitfieldData, 0, bitfieldData.Length);

        // Преобразуем байты в биты
        int totalBits = bitfieldData.Length * 8;
        int availableCount = 0;

        for (int i = 0; i < Math.Min(availablePieces.Length, totalBits); i++)
        {
            int byteIndex = i / 8;
            int bitIndex = 7 - (i % 8); // BitTorrent использует big-endian биты
            bool hasPiece = (bitfieldData[byteIndex] & (1 << bitIndex)) != 0;

            availablePieces[i] = hasPiece;
            if (hasPiece) availableCount++;
        }

        Console.WriteLine($"[BitField] Peer has {availableCount}/{availablePieces.Length} pieces available");
    }

    private static async Task ReadUnchoke(NetworkStream stream)
    {
        try
        {
            var lengthBytes = new byte[4];
            await ReadFullAsync(stream, lengthBytes, 4);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

            if (length > 0)
            {
                var message = new byte[length];
                await ReadFullAsync(stream, message, length);

                byte messageId = message[0];

                if (messageId == 1)
                {
                    Console.WriteLine("[Protocol] Peer unchoked us - download allowed");
                    return;
                }
                else if (messageId == 0)
                {
                    throw new Exception("Peer choked us - download not allowed");
                }
                else
                {
                    Console.WriteLine($"[Protocol] Received message {messageId}, waiting for unchoke...");
                    await ReadUnchoke(stream);
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to get unchoke: {ex.Message}");
        }
    }

    private static async Task<byte[]> DownloadPieceData(NetworkStream stream, int pieceIndex, int pieceLength)
    {
        var pieceData = new byte[pieceLength];
        int bytesDownloaded = 0;
        int blockSize = 16384;

        while (bytesDownloaded < pieceLength)
        {
            int requestSize = Math.Min(blockSize, pieceLength - bytesDownloaded);

            // Отправляем Request сообщение
            var request = CreateRequestMessage(pieceIndex, bytesDownloaded, requestSize);
            await stream.WriteAsync(request);

            // Читаем Piece сообщение
            var pieceMessage = await ReadPieceMessage(stream);
            if (pieceMessage != null)
            {
                Buffer.BlockCopy(pieceMessage, 0, pieceData, bytesDownloaded, pieceMessage.Length);
                bytesDownloaded += pieceMessage.Length;
            }
        }

        return pieceData;
    }

    private static async Task<byte[]?> ReadPieceMessage(NetworkStream stream)
    {
        while (true)
        {
            byte[] message;
            var lengthBytes = new byte[4];
            await ReadFullAsync(stream, lengthBytes, 4);
            int length = IPAddress.NetworkToHostOrder(BitConverter.ToInt32(lengthBytes, 0));

            if (length == 0)
            {
                Console.WriteLine("[Protocol] Keep-alive");
                continue; // Пропускаем keep-alive
            }

            if (length < 9)
            {
                // Это НЕ piece сообщение (piece должно быть >= 9 байт)
                message = new byte[length];
                await ReadFullAsync(stream, message, length);

                byte messageId = message[0];
                Console.WriteLine($"[Protocol] Non-piece message: ID={messageId}, length={length}");

                if (messageId == 0) // Choke
                    throw new Exception("Peer choked us during download");
                else if (messageId == 1) // Unchoke
                    Console.WriteLine("[Protocol] Received unchoke (already unchoked)");
                else
                    Console.WriteLine($"[Protocol] Other message: {messageId}");

                continue; // Читаем следующее сообщение
            }

            if (length > 1000000)
            {
                throw new Exception($"Message too long: {length} bytes");
            }

            // Это может быть piece сообщение
            message = new byte[length];
            await ReadFullAsync(stream, message, length);

            if (message[0] == 7) // Piece
            {
                var data = new byte[length - 9];
                Buffer.BlockCopy(message, 9, data, 0, data.Length);
                return data;
            }
            else
            {
                // Не piece, продолжаем читать
                Console.WriteLine($"[Protocol] Expected piece but got ID: {message[0]}");
                continue;
            }
        }
    }

    private static async Task ReadFullAsync(NetworkStream stream, byte[] buffer, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead));
            if (read == 0) throw new Exception("Connection closed");
            totalRead += read;
        }
    }

    private static byte[] CreateHandshake(byte[] infoHash, string peerId)
    {
        var handshake = new byte[68];
        handshake[0] = 19;                                                      // 20 байт для строки "BitTorrent protocol"
        Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(handshake, 1);
        new byte[8].CopyTo(handshake, 20);                                      // 8 байт резерва
        infoHash.CopyTo(handshake, 28);                                         // 20 байт для InfoHash из .torrent файла
        Encoding.ASCII.GetBytes(peerId).CopyTo(handshake, 48);                  // 20 байт для идентификатора пира peer_id
        return handshake;
    }

    private static byte[] CreateRequestMessage(int pieceIndex, int begin, int length)
    {
        var message = new byte[17];
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(13)).CopyTo(message, 0);         // 4 байта - длина сообщения
        message[4] = 6;                                                                     // 1 байт - ID запроса
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(pieceIndex)).CopyTo(message, 5); // 4 байта - индекс запрашиваемого куска
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(begin)).CopyTo(message, 9);      // 4 байта - смещение от начала куска (байты)
        BitConverter.GetBytes(IPAddress.HostToNetworkOrder(length)).CopyTo(message, 13);    // 4 байта - сколько запрашиваем байт
        return message;
    }

    public void Dispose()
    {
        _stream?.Close();
        _client?.Close();
        _stream?.Dispose();
        _client?.Dispose();
        GC.SuppressFinalize(this);
    }
}*/