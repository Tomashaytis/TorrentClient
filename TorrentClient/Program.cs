using TorrentClient.Domain.Core;
using TorrentClient.Tracker;

namespace TorrentClient;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var torrentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Resources", args[0]);
        torrentPath = Path.GetFullPath(torrentPath);
        Console.WriteLine($"[info] Loading torrent: {torrentPath}");

        try
        {
            var meta = await TorrentMetadata.LoadAsync(torrentPath);
            Console.WriteLine($"[info] Name    : {meta.Name}");
            Console.WriteLine($"[info] Length  : {meta.Length} bytes");
            Console.WriteLine($"[info] Tracker : {meta.Announce}");
            if (!string.IsNullOrWhiteSpace(meta.Comment))
                Console.WriteLine($"[info] Comment : {meta.Comment}");
            if (!string.IsNullOrWhiteSpace(meta.CreatedBy))
                Console.WriteLine($"[info] Created by : {meta.CreatedBy}");
            Console.WriteLine($"[info] InfoHash: {BitConverter.ToString(meta.InfoHash).Replace("-", "")}");

            var peerId = PeerIdGenerator.Generate("-CS0001-");
            var port = 6881;

            var tracker = new TrackerClient(meta.Announce);
            var peers = await tracker.AnnounceAsync(
                infoHash: meta.InfoHash,
                peerId: peerId,
                port: port,
                downloaded: 0,
                uploaded: 0,
                left: meta.Length,
                compact: false
            );

            Console.WriteLine($"\n Peers from tracker ({peers.Count}):");
            foreach (var p in peers) Console.WriteLine($"  - {p}");

            Console.WriteLine("\n Async HTTP connection established successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] Error: {ex.Message}");
            return 1;
        }

    }
}