using System.Net;
using TorrentClient.Domain.Core;

namespace TorrentClient.Download;


public class DownloadManager(TorrentMetadata metadata, List<IPEndPoint> peers, string peerId)
{
    public string PeerId { get; private set; } = peerId;
    public int PieceLength { get; private set; } = (int)metadata.PieceLength;
    public int TotalPieces { get; private set; } = metadata.Pieces.Length;
    private readonly byte[] _infoHash = metadata.InfoHash;

    private readonly List<IPEndPoint> _peers = peers;
    private readonly TorrentMetadata _metadata = metadata;
    private readonly object _fileWriteLock = new();
    private readonly object _progressLock = new object();
    private long _downloadedCount = 0;

    public async Task DownloadFileAsync(string downloadPath, int maxTasksCount = 1)
    {
        Console.WriteLine($"[Download] Starting download: {TotalPieces} pieces, {PieceLength} bytes each");

        using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write);
        fileStream.SetLength(_metadata.Length);

        var downloadedPieces = new bool[TotalPieces];
        var tasks = new List<Task>();

        var pieceDownloaders = new List<PieceDownloader>();

        foreach (var peer in _peers)
        {
            try
            {
                pieceDownloaders.Add(await PieceDownloader.CreateAsync(_infoHash, PeerId, peer, TotalPieces));
            }
            catch (Exception) {}
        }

        for (int pieceIndex = 0; pieceIndex < TotalPieces; pieceIndex++)
        {
            int currentPiece = pieceIndex;
            tasks.Add(DownloadPieceAsync(currentPiece, fileStream, downloadedPieces, pieceDownloaders));

            if (tasks.Count >= maxTasksCount)
            {
                await Task.WhenAny(tasks);
                tasks.RemoveAll(t => t.IsCompleted);
            }
        }

        await Task.WhenAll(tasks);
        Console.WriteLine("[Download] Download completed!");
    }

    private async Task DownloadPieceAsync(int pieceIndex, FileStream fileStream, bool[] downloadedPieces, List<PieceDownloader> pieceDownloaders)
    {
        if (downloadedPieces[pieceIndex])
            return;

        Console.WriteLine($"[Piece {pieceIndex}] Starting download...");

        foreach (var downloader in pieceDownloaders)
        {
            try
            {
                if (!downloader.HasPiece(pieceIndex))
                {
                    Console.WriteLine($"[Piece {pieceIndex}] Peer {downloader.Peer} doesn't have this piece");
                    continue;
                }

                var pieceData = await downloader.DownloadPieceFromPeer(pieceIndex, PieceLength);
                if (pieceData != null && pieceData.Length > 0)
                {
                    lock (_fileWriteLock)
                    {
                        long fileOffset = pieceIndex * (long)PieceLength;
                        fileStream.Seek(fileOffset, SeekOrigin.Begin);
                        fileStream.Write(pieceData, 0, pieceData.Length);
                    }
                    downloadedPieces[pieceIndex] = true;

                    UpdateProgress(pieceIndex);
                    await Task.Delay(100);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Piece {pieceIndex}] Failed from {downloader.Peer}: {ex.Message}");
            }
        }
        Console.WriteLine($"[Piece {pieceIndex}] Failed to download from all peers");
    }

    private void UpdateProgress(int pieceIndex)
    {
        lock (_progressLock)
        {
            _downloadedCount++;
            double percent = (double)_downloadedCount / TotalPieces * 100;
            Console.WriteLine($"[Progress] {_downloadedCount}/{TotalPieces} ({percent:F2}%) - Piece {pieceIndex}");
        }
    }
}
