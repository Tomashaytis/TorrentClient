using System.Net;
using TorrentClient.Domain.Core;

namespace TorrentClient.Download;


public class DownloadManager(TorrentMetadata metadata, List<IPEndPoint> peers, string peerId)
{
    public string PeerId { get; private set; } = peerId;
    public int PieceLength { get; private set; } = (int)metadata.PieceLength;
    public int TotalPieces { get; private set; } = (int)Math.Ceiling((double)metadata.Length / metadata.PieceLength);
    private readonly byte[] _infoHash = metadata.InfoHash;

    private readonly List<IPEndPoint> _peers = peers;
    private readonly TorrentMetadata _metadata = metadata;
    private readonly object _fileWriteLock = new();
    private readonly object _progressLock = new();
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
            tasks.Add(DownloadPieceAsync(currentPiece, fileStream, downloadedPieces, pieceDownloaders[..1]));

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
        lock (downloadedPieces)
        {
            if (downloadedPieces[pieceIndex]) return;
        }

        int actualPieceLength = GetPieceSize(pieceIndex);

        foreach (var downloader in pieceDownloaders)
        {
            try
            {
                if (!downloader.HasPiece(pieceIndex)) continue;

                var pieceData = await downloader.DownloadPieceFromPeer(pieceIndex, actualPieceLength);

                if (pieceData != null && pieceData.Length == actualPieceLength)
                {
                    if (!VerifyPieceHash(pieceIndex, pieceData))
                    {
                        Console.WriteLine($"[Piece {pieceIndex}] Hash verification FAILED!");
                        continue;
                    }

                    lock (_fileWriteLock)
                    {
                        if (downloadedPieces[pieceIndex]) return;

                        long fileOffset = pieceIndex * (long)PieceLength;
                        fileStream.Seek(fileOffset, SeekOrigin.Begin);
                        fileStream.Write(pieceData, 0, actualPieceLength);
                        downloadedPieces[pieceIndex] = true;
                    }

                    UpdateProgress(pieceIndex);
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Piece {pieceIndex}] Failed: {ex.Message}");
            }
        }
    }

    private bool VerifyPieceHash(int pieceIndex, byte[] pieceData)
    {
        using var sha1 = System.Security.Cryptography.SHA1.Create();
        byte[] computedHash = sha1.ComputeHash(pieceData);

        const int HASH_SIZE = 20;
        int offset = pieceIndex * HASH_SIZE;

        if (offset + HASH_SIZE > _metadata.Pieces.Length)
        {
            Console.WriteLine($"[Piece {pieceIndex}] Invalid piece index");
            return false;
        }

        byte[] expectedHash = new byte[HASH_SIZE];
        Buffer.BlockCopy(_metadata.Pieces, offset, expectedHash, 0, HASH_SIZE);

        bool isValid = computedHash.SequenceEqual(expectedHash);

        if (!isValid)
        {
            Console.WriteLine($"[Piece {pieceIndex}] Hash mismatch! Retrying...");
            Console.WriteLine($"  Expected: {BitConverter.ToString(expectedHash)}");
            Console.WriteLine($"  Got:      {BitConverter.ToString(computedHash)}");
        }
        else
        {
            Console.WriteLine($"[Piece {pieceIndex}] Hash verified OK");
        }

        return isValid;
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

    private int GetPieceSize(int pieceIndex)
    {
        if (pieceIndex == TotalPieces - 1)
        {
            return (int)(_metadata.Length - (pieceIndex * (long)PieceLength));
        }
        return PieceLength;
    }
}

public record PieceTask(int pieceIndex, int pieceLength, PieceDownloader pieceDownloader, bool downloaded = true);