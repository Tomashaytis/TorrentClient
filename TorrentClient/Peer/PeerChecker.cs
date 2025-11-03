using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TorrentClient.Peer;

public class PeerChecker(byte[] infoHash, string peerId)
{
    public string PeerId { get; private set; } = peerId;
    private readonly byte[] _infoHash = infoHash;

    public async Task<bool> PerformHandshakeAsync(IPEndPoint peer)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(peer.Address, peer.Port);

        using var stream = client.GetStream();

        stream.ReadTimeout = 1000;
        stream.WriteTimeout = 1000;

        // Создание и отправка handshake
        var handshake = CreateHandshake();
        await stream.WriteAsync(handshake);

        // Получение ответа
        var response = new byte[68];
        await stream.ReadAsync(response);

        // Проверка совпадения infoHash
        var responseInfoHash = response[28..48];
        return _infoHash.SequenceEqual(responseInfoHash);
    }

    public byte[] CreateHandshake()
    {
        var handshake = new byte[68];
        handshake[0] = 19;                                                      // 20 байт для строки "BitTorrent protocol"
        Encoding.ASCII.GetBytes("BitTorrent protocol").CopyTo(handshake, 1);
        new byte[8].CopyTo(handshake, 20);                                      // 8 байт резерва
        _infoHash.CopyTo(handshake, 28);                                        // 20 байт для InfoHash из .torrent файла
        Encoding.ASCII.GetBytes(PeerId).CopyTo(handshake, 48);                  // 20 байт для идентификатора пира peer_id
        return handshake;
    }
}