using System.Net;
using TorrentClient.Domain.Core;
using TorrentClient.Download;
using TorrentClient.Peer;
using TorrentClient.Tracker;

namespace TorrentClient;

public class Program
{
    public static readonly List<string> HttpTrackers =
    [
        "https://yuki.bt.bontal.net:443/announce",
        "https://tracker.zhuqiy.com:443/announce",
        "https://tracker.yemekyedim.com:443/announce",
        "https://tracker.pmman.tech:443/announce",
        "https://tracker.leechshield.link:443/announce",
        "https://tracker.ghostchu-services.top:443/announce",
        "https://tracker.cutie.dating:443/announce",
        "https://tracker.bt4g.com:443/announce",
        "https://tr.nyacat.pw:443/announce",
        "https://torrent.tracker.durukanbal.com:443/announce",
        "https://bt.bontal.net:443/announce",
        "https://tracker.uraniumhexafluori.de:443/announce",
        "https://tracker.moeblog.cn:443/announce",
        "https://tracker.gcrenwp.top:443/announce",
        "https://tracker.belmult.online:443/announce",
        "https://tracker.alaskantf.com:443/announce",
        "https://t.213891.xyz:443/announce",
        "https://shahidrazi.online:443/announce",
        "http://wepzone.net:6969/announce",
        "http://tracker2.dler.org:80/announce",
        "http://tracker1.bt.moack.co.kr:80/announce",
        "http://tracker.zhuqiy.com:80/announce",
        "http://tracker.xiaoduola.xyz:6969/announce",
        "http://tracker.waaa.moe:6969/announce",
        "http://tracker.tritan.gg:8080/announce",
        "http://tracker.skyts.net:6969/announce",
        "http://tracker.sbsub.com:2710/announce",
        "http://tracker.renfei.net:8080/announce",
        "http://tracker.qu.ax:6969/announce",
        "http://tracker.mywaifu.best:6969/announce",
        "http://tracker.ghostchu-services.top:80/announce",
        "http://tracker.dmcomic.org:2710/announce",
        "http://tracker.dler.org:6969/announce",
        "http://tracker.dhitechnical.com:6969/announce",
        "http://tracker.darkness.services:6969/announce",
        "http://tracker.cutie.dating:80/announce",
        "http://tracker.bz:80/announce",
        "http://tracker.bt4g.com:2095/announce",
        "http://tracker.bt-hash.com:80/announce",
        "http://tracker.23794.top:6969/announce",
        "http://tr.nyacat.pw:80/announce",
        "http://t.overflow.biz:6969/announce",
        "http://shubt.net:2710/announce",
        "http://retracker.spark-rostov.ru:80/announce",
        "http://public.tracker.vraphim.com:6969/announce",
        "http://open.trackerlist.xyz:80/announce",
        "http://open.acgtracker.com:1096/announce",
        "http://extracker.dahrkael.net:6969/announce",
        "http://bvarf.tracker.sh:2086/announce",
        "http://buny.uk:6969/announce",
        "http://bt1.xxxxbt.cc:6969/announce",
        "http://bittorrent-tracker.e-n-c-r-y-p-t.net:1337/announce",
        "http://0d.kebhana.mx:443/announce",
        "http://0123456789nonexistent.com:80/announce",
        "http://www.torrentsnipe.info:2701/announce",
        "http://www.genesis-sp.org:2710/announce",
        "http://tracker810.xyz:11450/announce",
        "http://tracker.vanitycore.co:6969/announce",
        "http://tracker.moxing.party:6969/announce",
        "http://tracker.lintk.me:2710/announce",
        "http://tracker.ipv6tracker.org:80/announce",
        "http://tracker.corpscorp.online:80/announce",
        "http://tracker.bittor.pw:1337/announce",
        "http://torrent.hificode.in:6969/announce",
        "http://share.hkg-fansub.info:80/announce.php",
        "http://servandroidkino.ru:80/announce",
        "http://seeders-paradise.org:80/announce",
        "http://p4p.arenabg.com:1337/announce",
        "http://home.yxgz.club:6969/announce",
        "http://bt.rer.lol:2710/announce",
        "http://bt.poletracker.org:2710/announce",
        "http://aboutbeautifulgallopinghorsesinthegreenpasture.online:80/announce",
        "http://1337.abcvg.info:80/announce"
    ];

    public static async Task<int> Main(string[] args)
    {
        var torrentPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", args[0]);
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
            Console.WriteLine($"[info] Piece length : {meta.PieceLength}");

            var peerId = PeerIdGenerator.GenerateStandardPeerId();
            var port = 6881;

            Console.WriteLine();
            Console.WriteLine($"[info] Peer ID: {peerId}");
            Console.WriteLine($"[info] Port: {port}");
            

            foreach (var trackerUrl in HttpTrackers)
            {
                Console.WriteLine();
                Console.WriteLine($"[info] Tracker: {trackerUrl}");
                try
                {
                    ITrackerClient tracker;
                    if (trackerUrl.StartsWith("http"))
                        tracker = new HttpTrackerClient(trackerUrl);
                    else if (trackerUrl.StartsWith("udp"))
                        tracker = new UdpTrackerClient(trackerUrl);
                    else
                        throw new NotImplementedException();

                    var peers = await tracker.AnnounceAsync(
                            infoHash: meta.InfoHash,
                            peerId: peerId,
                            port: port,
                            downloaded: 0,
                            uploaded: 0,
                            left: meta.Length,
                            compact: true
                    );

                    if (peers.Count == 0)
                        continue;

                    Console.WriteLine($"\n Peers from tracker ({peers.Count}):");
                    foreach (var peer in peers)
                        Console.WriteLine($"  - {peer}");

                    Console.WriteLine("\n Async HTTP connection established successfully.");

                    var pieceDownloader = new PeerChecker(meta.InfoHash, peerId);

                    var handshakeTasks = new List<Task<(IPEndPoint peer, bool success)>>();
                    foreach (var peer in peers)
                    {
                        if (peer.Address.Equals(IPAddress.Loopback) || peer.Address.ToString() == "46.0.121.80")
                            continue;

                        handshakeTasks.Add(TryHandshakeAsync(pieceDownloader, peer));
                    }

                    var results = await Task.WhenAll(handshakeTasks);

                    var successfulPeers = results.Where(r => r.success).Select(r => r.peer).ToList();

                    Console.WriteLine();
                    Console.WriteLine($"Successful handshakes: {successfulPeers.Count}/{peers.Count}");
                    foreach (var peer in successfulPeers)
                    {
                        Console.WriteLine($"  + {peer}");
                    }
                    Console.WriteLine();

                    if (successfulPeers.Count != 0)
                    {
                        Console.WriteLine($"Starting download with {successfulPeers.Count} peers...");

                        var downloadManager = new DownloadManager(meta, successfulPeers, peerId);
                        var downloadPath = Path.Combine(Environment.CurrentDirectory, "downloads", meta.Name);

                        var dirPath = Path.GetDirectoryName(downloadPath);
                        if (dirPath != null)
                            _ = Directory.CreateDirectory(dirPath);
                        await downloadManager.DownloadFileAsync(downloadPath);

                        Console.WriteLine($"File downloaded to: {downloadPath}");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[X] Error: {ex.Message}");
                }
            } 
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] Error: {ex.Message}");
            return 1;
        }

    }

    async static Task<(IPEndPoint peer, bool success)> TryHandshakeAsync(PeerChecker client, IPEndPoint peer)
    {
        try
        {
            var success = await client.PerformHandshakeAsync(peer);
            Console.WriteLine($"[info] Handshake with {peer}: {(success ? "SUCCESS" : "FAILED")}");
            return (peer, success);
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"[X] Handshake with {peer}: FAILED");
            return (peer, false);
        }
    }
}