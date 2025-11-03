using System.Net;
using System.Text;
using TorrentClient.Domain.Parser;

namespace TorrentClient.Tracker;

public class HttpTrackerClient : ITrackerClient
{
    private readonly string _announce;
    private readonly HttpClient _http = new();

    public HttpTrackerClient(string announceUrl)
    {
        _announce = announceUrl;
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        _http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    public async Task<List<IPEndPoint>> AnnounceAsync(byte[] infoHash, string peerId, int port, long downloaded, long uploaded, long left, bool compact)
    {
        var queryParams = new List<string>
        {
            $"info_hash={PercentEncode(infoHash)}",
            $"peer_id={PercentEncode(Encoding.ASCII.GetBytes(peerId))}",
            $"port={port}",
            $"uploaded={uploaded}",
            $"downloaded={downloaded}",
            $"left={left}",
            $"compact={(compact ? 1 : 0)}",
            "event=started"
        };

        var queryString = string.Join("&", queryParams);
        var url = $"{_announce}?{queryString}";

        var resp = await _http.GetAsync(url);

        if (!resp.IsSuccessStatusCode)
        {
            _ = await resp.Content.ReadAsStringAsync();
            throw new HttpRequestException($"HTTP {resp.StatusCode}: {resp.ReasonPhrase}");
        }

        var bytes = await resp.Content.ReadAsByteArrayAsync();

        using var ms = new MemoryStream(bytes);

        var parser = new BencodeParser(ms);
        var root = (Dictionary<string, object>)parser.Parse();

        if (root.TryGetValue("failure reason", out object? value))
        {
            string failure =
                value is byte[] b ? Encoding.UTF8.GetString(b) :
                value?.ToString() ?? "Unknown failure";
            throw new InvalidOperationException($"Tracker failure: {failure}");
        }

        return ParsePeers(root);
    }

    private static string PercentEncode(byte[] data)
    {
        var sb = new StringBuilder();
        foreach (var b in data)
        {
            sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private static List<IPEndPoint> ParsePeers(Dictionary<string, object> root)
    {
        var peers = new List<IPEndPoint>();

        if (!root.TryGetValue("peers", out object? peersObj))
        {
            return peers;
        }

        if (peersObj is List<object> peerList)
        {
            foreach (var p in peerList)
            {
                if (p is Dictionary<string, object> peerDict)
                {
                    try
                    {
                        var ipBytes = (byte[])peerDict["ip"];
                        var ipStr = Encoding.ASCII.GetString(ipBytes);
                        var peerPort = (long)peerDict["port"];

                        if (IPAddress.TryParse(ipStr, out IPAddress? ip))
                        {
                            peers.Add(new IPEndPoint(ip, (int)peerPort));
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error parsing peer: {ex.Message}");
                    }
                }
            }
        }
        else if (peersObj is byte[] peerBytes)
        {
            ParseCompactPeers(peerBytes, peers);
        }
        else if (peersObj is string peerString)
        {
            var newPeerBytes = Encoding.Latin1.GetBytes(peerString);
            ParseCompactPeers(newPeerBytes, peers);
        }
        else
        {
            Console.WriteLine($"Unknown peers format: {peersObj.GetType()}");
        }

        return peers;
    }

    private static void ParseCompactPeers(byte[] peerBytes, List<IPEndPoint> peers)
    {
        // В компактном формате каждый пир - это 6 байт (4 IP + 2 порт)
        for (int i = 0; i + 5 <= peerBytes.Length; i += 6)
        {
            try
            {
                var ipBytes = new byte[4];
                Array.Copy(peerBytes, i, ipBytes, 0, 4);
                var ip = new IPAddress(ipBytes);

                var port = (peerBytes[i + 4] << 8) | peerBytes[i + 5];
                peers.Add(new IPEndPoint(ip, port));

            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing compact peer at offset {i}: {ex.Message}");
            }
        }
    }

}
