using System.Text;

namespace TorrentClient.Domain.Parser;

public class BencodeParser(Stream stream)
{
    public Stream Stream { get; private set; } = stream;

    public object Parse()
    {
        var curSymbol = NextSymbol();

        return curSymbol switch
        {
            'i' => ParseInteger(),
            'l' => ParseList(),
            'd' => ParseDictionary(),
            '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' => ParseString(curSymbol),
            _ => throw new FormatException($"Unknown prefix: {curSymbol}"),
        };
    }

    public object Parse(char curSymbol)
    {
        return curSymbol switch
        {
            'i' => ParseInteger(),
            'l' => ParseList(),
            'd' => ParseDictionary(),
            '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' => ParseString(curSymbol),
            _ => throw new FormatException($"Unknown prefix: {curSymbol}"),
        };
    }

    public byte[] ParseString(char startSymbol)
    {
        var bytesCountStr = "" + startSymbol;
        char c;
        do
        {
            c = NextSymbol();
            if (c != ':' && (c < '0' || c > '9'))
                throw new FormatException($"Invalid symbol in byte count value: {c}");
            if (c != ':') bytesCountStr += c;
        } while (c != ':');

        int n = int.Parse(bytesCountStr);
        var buf = new byte[n];
        int read = 0;
        while (read < n)
        {
            int r = Stream.Read(buf, read, n - read);
            if (r <= 0) throw new EndOfStreamException();
            read += r;
        }
        return buf;
    }
    public long ParseInteger()
    {
        var sb = new StringBuilder();
        bool first = true;
        char c;
        while ((c = NextSymbol()) != 'e')
        {
            if (first && c == '-') { sb.Append(c); first = false; continue; }
            if (c < '0' || c > '9')
                throw new FormatException($"Invalid digit: {c}");
            sb.Append(c);
            first = false;
        }
        if (sb.Length == 0) throw new FormatException("Integer value not found");
        return long.Parse(sb.ToString());
    }

    public List<object> ParseList()
    {
        var list = new List<object>();
        char curSymbol;
        do
        {
            curSymbol = NextSymbol();

            if (curSymbol != 'e')
                list.Add(Parse(curSymbol));
        } while (curSymbol != 'e');

        return list;
    }

    public Dictionary<string, object> ParseDictionary()
    {
        var dictionary = new Dictionary<string, object>();
        string? previousKey = null;

        char curSymbol;
        do
        {
            curSymbol = NextSymbol();

            if (curSymbol != 'e') {
                var keyBytes = (byte[])ParseString(curSymbol);
                var key = Encoding.ASCII.GetString(keyBytes);

                if (key is string stringKey)
                {
                    if (stringKey == "")
                        throw new FormatException($"Empty string is used as key value");

                    curSymbol = NextSymbol();
                    if (curSymbol == 'e')
                        throw new FormatException($"Missing value for key '{stringKey}'");

                    if (dictionary.ContainsKey(stringKey))
                        throw new FormatException($"Key '{stringKey}' already exists in dictionary");

                    if (previousKey != null && string.Compare(previousKey, stringKey, StringComparison.Ordinal) >= 0)
                        throw new FormatException($"Keys are not sorted: '{previousKey}' should come before '{stringKey}'");

                    dictionary[stringKey] = Parse(curSymbol);
                }
            }
        } while (curSymbol != 'e');

        return dictionary;
    }

    public char NextSymbol()
    {
        var nextByte = Stream.ReadByte();
        if (nextByte == -1)
            throw new EndOfStreamException();
        return (char)nextByte;
    }
}

