using TorrentClient.Domain.Parser;

namespace TorrentClient;

internal class Program
{
    static void Main(string[] args)
    {
        var stream = File.OpenRead(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", args[0]));

        var data = new BencodeParser(stream).Parse();

        Console.WriteLine();
    }
}