using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TorrentClient.Domain.Core;

public class PeerIdGenerator
{
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
