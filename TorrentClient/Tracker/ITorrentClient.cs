using System.Net;

namespace TorrentClient.Tracker;

public interface ITrackerClient
{
    Task<List<IPEndPoint>> AnnounceAsync(
        byte[] infoHash,
        string peerId,
        int port,
        long downloaded,
        long uploaded,
        long left,
        bool compact
        );
}