using System.Net;
using System.Text;
using TorrentClient.Domain.Parser;

namespace TorrentClient.Tracker;

public class TrackerClient
{
    private readonly string _announce;
    private readonly HttpClient _http = new HttpClient();

    public TrackerClient(string announceUrl) => _announce = announceUrl;

    public async Task<List<IPEndPoint>> AnnounceAsync(byte[] infoHash, string peerId, int port, long downloaded, long uploaded, long left, bool compact)
    {
        var uri = BuildAnnounceUri(_announce, infoHash, peerId, port, downloaded, uploaded, left, compact);
        var resp = await _http.GetAsync(uri);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync();

        using var ms = new MemoryStream(bytes);
        var parser = new BencodeParser(ms);
        var root = (Dictionary<string, object>)parser.Parse();

        if (root.ContainsKey("failure reason"))
            throw new InvalidOperationException($"Tracker failure: {(string)root["failure reason"]}");

        var peers = new List<IPEndPoint>();

        if (!compact)
        {
            var list = (List<object>)root["peers"];
            foreach (var p in list)
            {
                var d = (Dictionary<string, object>)p;
                peers.Add(new IPEndPoint(IPAddress.Parse((string)d["ip"]), (int)(long)d["port"]));
            }
        }
        else
        {
            throw new NotSupportedException("Compact peers not supported with current parser.");
        }

        return peers;
    }

    private static Uri BuildAnnounceUri(
        string announce, byte[] infoHash, string peerId, int port,
        long downloaded, long uploaded, long left, bool compact)
    {
        var sb = new StringBuilder();
        sb.Append(announce);
        sb.Append(announce.Contains('?') ? '&' : '?');
        sb.Append("info_hash=").Append(PercentEncode(infoHash));
        sb.Append("&peer_id=").Append(PercentEncode(Encoding.ASCII.GetBytes(peerId)));
        sb.Append("&port=").Append(port);
        sb.Append("&uploaded=").Append(uploaded);
        sb.Append("&downloaded=").Append(downloaded);
        sb.Append("&left=").Append(left);
        sb.Append("&compact=").Append(compact ? "1" : "0");
        sb.Append("&event=started");
        return new Uri(sb.ToString());
    }

    private static string PercentEncode(byte[] data)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            if ((b >= 0x30 && b <= 0x39) || (b >= 0x41 && b <= 0x5A) ||
                (b >= 0x61 && b <= 0x7A) || b is 0x2D or 0x2E or 0x5F or 0x7E)
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}
