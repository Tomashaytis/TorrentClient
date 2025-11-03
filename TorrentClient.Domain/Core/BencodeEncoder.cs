using System.Text;

namespace TorrentClient.Domain.Core;

public class BencodeEncoder
{
    public static byte[] Encode(object value)
    {
        using var ms = new MemoryStream();
        EncodeToStream(value, ms);
        return ms.ToArray();
    }

    private static void EncodeToStream(object value, Stream s)
    {
        switch (value)
        {
            case string str:
                var bytes = Encoding.UTF8.GetBytes(str);
                WriteAscii(s, bytes.Length.ToString());
                s.WriteByte((byte)':');
                s.Write(bytes, 0, bytes.Length);
                break;

            case long l:
                WriteAscii(s, "i"); WriteAscii(s, l.ToString()); WriteAscii(s, "e");
                break;

            case int i:
                WriteAscii(s, "i"); WriteAscii(s, i.ToString()); WriteAscii(s, "e");
                break;

            case List<object> list:
                WriteAscii(s, "l");
                foreach (var item in list) EncodeToStream(item, s);
                WriteAscii(s, "e");
                break;

            case Dictionary<string, object> dict:
                WriteAscii(s, "d");
                foreach (var kv in dict.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    EncodeToStream(kv.Key, s);
                    EncodeToStream(kv.Value, s);
                }
                WriteAscii(s, "e");
                break;
            case byte[] raw:
                // bencode string = "<len>:<bytes>"
                WriteAscii(s, raw.Length.ToString());
                s.WriteByte((byte)':');
                s.Write(raw, 0, raw.Length);
                break;
            default:
                throw new NotSupportedException($"Unsupported bencode type: {value.GetType()}");
        }
    }

    private static void WriteAscii(Stream s, string ascii)
    {
        var bytes = Encoding.ASCII.GetBytes(ascii);
        s.Write(bytes, 0, bytes.Length);
    }
}
