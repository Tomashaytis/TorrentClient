using System.Security.Cryptography;
using System.Text;
using TorrentClient.Domain.Parser;

namespace TorrentClient.Domain.Core;

public class TorrentMetadata
{
    public required string Announce { get; init; }
    public required List<string> AnnounceList { get; init; }
    public required string Name { get; init; }
    public required long Length { get; init; }
    public required long PieceLength { get; init; }
    public required byte[] Pieces { get; init; }
    public required byte[] InfoHash { get; init; }
    public string? Comment { get; init; }
    public string? CreatedBy { get; init; }
    public required Dictionary<string, object> Info { get; init; }

    static string Ascii(object o) => Encoding.ASCII.GetString((byte[])o);
    static string Utf8(object o) => Encoding.UTF8.GetString((byte[])o);

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

        var announce = Ascii(root["announce"]);
        var comment = root.TryGetValue("comment", out var cmt) ? Utf8(cmt) : "";
        var createdBy = root.TryGetValue("created by", out var cb) ? Utf8(cb) : "";
        var pieceLength = (long)((Dictionary<string, object>)root["info"])["piece length"];
        var pieces = (byte[])((Dictionary<string, object>)root["info"])["pieces"];


        var resAnnounceList = new List<string>();
        if (root.TryGetValue("announce-list", out var alObj) && alObj is List<object> tiers)
        {
            foreach (var tierObj in tiers)
            {
                if (tierObj is List<object> tier && tier.Count > 0 && tier[0] is byte[] urlBytes)
                    resAnnounceList.Add(Encoding.ASCII.GetString(urlBytes));
            }
        }
        else
            resAnnounceList.Add(announce);

        if (!root.TryGetValue("info", out var infoObj))
            throw new FormatException("Missing 'info' dictionary.");

        var info = (Dictionary<string, object>)infoObj;
        string name = Utf8(info["name"]);


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
            PieceLength = pieceLength,
            Pieces = pieces,
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
