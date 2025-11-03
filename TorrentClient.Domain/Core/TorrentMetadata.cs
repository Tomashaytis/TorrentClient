using System.Security.Cryptography;
using TorrentClient.Domain.Parser;

namespace TorrentClient.Domain.Core;

public class TorrentMetadata
{
    public required string Announce { get; init; }
    public required List<string> AnnounceList { get; init; }
    public required string Name { get; init; }
    public required long Length { get; init; }
    public required byte[] InfoHash { get; init; }
    public string? Comment { get; init; }
    public string? CreatedBy { get; init; }
    public required Dictionary<string, object> Info { get; init; }

    public static async Task<TorrentMetadata> LoadAsync(string path)
    {
        var raw = await File.ReadAllBytesAsync(path);

        try
        {
            return ParseOrThrow(raw);
        }
        catch (Exception ex1)
        {
            throw new FormatException($"Cannot parse .torrent file.\nRaw error: {ex1.Message}");
        }
           
    }

    private static TorrentMetadata ParseOrThrow(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        var parser = new BencodeParser(ms);
        var root = (Dictionary<string, object>)parser.Parse();      

        string announce = GetString(root, "announce");
        var announceList = (List<object>)root["announce-list"];

        var resAnnounceList = new List<string>();
        foreach (var item in announceList)
        {
            try
            {
                resAnnounceList.Add((string)(((List<object>)item)[0]));
            }
            catch (Exception ex)
            {
            }
        }
        resAnnounceList.Add(announce);

        string comment = GetString(root, "comment");
        string createdBy = GetString(root, "created by");

        if (!root.TryGetValue("info", out var infoObj))
            throw new FormatException("Missing 'info' dictionary.");
        var info = (Dictionary<string, object>)infoObj;

        string name = GetString(info, "name");

        long length = GetTotalLength(info);

        var infoBytes = BencodeEncoder.Encode(info);
        var infoHash = SHA1.HashData(infoBytes);

        return new TorrentMetadata
        {
            Announce = announce,
            AnnounceList = resAnnounceList,
            Comment = comment,
            CreatedBy = createdBy,
            Name = name,
            Length = length,
            InfoHash = infoHash,
            Info = info
        };
    }

    private static string GetString(Dictionary<string, object> dict, string key)
        => dict.TryGetValue(key, out var v) ? (string)v : "";

    private static long GetTotalLength(Dictionary<string, object> info)
    {
        if (info.TryGetValue("length", out var len))
            return (long)len;

        if (info.TryGetValue("files", out var filesObj) && filesObj is List<object> filesList)
        {
            long sum = 0;
            foreach (var f in filesList)
            {
                var d = (Dictionary<string, object>)f;
                if (d.TryGetValue("length", out var fl)) sum += (long)fl;
            }
            return sum;
        }

        throw new FormatException("Neither 'length' nor 'files' present in info.");
    }
}
