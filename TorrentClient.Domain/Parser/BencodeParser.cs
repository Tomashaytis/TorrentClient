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

    public string ParseString(char startSymbol)
    {
        var bytesCountStr = "" + startSymbol;

        char curSymbol;
        do
        {
            curSymbol = NextSymbol();

            if (!"0123456789:".Contains(curSymbol)) {
                throw new FormatException($"Invalid symbol in byte count value: {curSymbol}");
            }

            if (curSymbol != ':')
                bytesCountStr += curSymbol;

        } while (curSymbol != ':');
        
        int bytesCount = int.Parse(bytesCountStr);

        var str = "";

        for (int i = 0; i < bytesCount; i++) 
        {
            str += NextSymbol();
        }

        return str;
    }

    public long ParseInteger()
    {
        var integerStr = "";

        bool first = true;
        char curSymbol;
        do
        {
            var curByte = NextSymbol();
            curSymbol = curByte;

            if (!"012345678e9".Contains(curSymbol))
            {
                if (curSymbol != '-' || !first)
                    throw new FormatException($"Invalid digit: {curSymbol}");
            }

            if (curSymbol != 'e')
                integerStr += curSymbol;

        } while (curSymbol != 'e');

        if (integerStr.Length == 0)
            throw new FormatException($"Integer value not found");

        long integer = long.Parse(integerStr);

        return integer;

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
                var key = Parse(curSymbol);
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

