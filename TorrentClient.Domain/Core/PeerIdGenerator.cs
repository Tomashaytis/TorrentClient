using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TorrentClient.Domain.Core;

public class PeerIdGenerator
{
    public static string GenerateStandardPeerId()
    {
        // Стандартный формат: -<клиент><версия>-<случайная часть>
        var clients = new[]
        {
            "qB", // qBittorrent
            "UT", // μTorrent
            "TR", // Transmission
            "DE", // Deluge
            "LT", // libtorrent
            "AX", // BitPump
            "TS", // Torrentstorm
        };

        var client = clients[Random.Shared.Next(clients.Length)];
        var version = GetRandomVersion(client);

        var remain = 20 - (client.Length + version.Length + 2);
        var randomPart = GenerateRandomPart(remain);

        return $"-{client}{version}-{randomPart}";
    }

    private static string GetRandomVersion(string client)
    {
        return client switch
        {
            "qB" => "43", // qBittorrent 4.3.x
            "UT" => "30", // μTorrent 3.0.x
            "TR" => "07", // Transmission 0.7.x
            "DE" => "10", // Deluge 1.0.x
            "LT" => "12", // libTorrent 1.2.x
            _ => "10"     // По умолчанию
        };
    }

    private static string GenerateRandomPart(int length)
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        var result = new char[length];

        for (int i = 0; i < length; i++)
            result[i] = chars[Random.Shared.Next(chars.Length)];

        return new string(result);
    }

    public static string Generate(string prefix)
    {
        var rnd = Random.Shared;
        var remain = 20 - prefix.Length;
        var buf = new char[remain];
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

        for (int i = 0; i < remain; i++)
            buf[i] = chars[rnd.Next(chars.Length)];

        return prefix + new string(buf);
    }
}
