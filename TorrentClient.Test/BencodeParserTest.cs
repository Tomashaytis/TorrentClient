using System.Text;
using TorrentClient.Domain.Parser;

namespace TorrentClient.Test;

public class TestBencodeParser
{
    [Theory]
    [MemberData(nameof(GetStringTestData))]
    public void TestParseString(string bencodeInput, string expected)
    {
        var bytes = Encoding.UTF8.GetBytes(bencodeInput);
        using var stream = new MemoryStream(bytes);

        var parser = new BencodeParser(stream);
        var actual = parser.Parse();

        Assert.IsType<byte[]>(actual);
        Assert.Equal(expected, Encoding.UTF8.GetString((byte[])actual));
    }

    [Theory]
    [MemberData(nameof(GetIntegerTestData))]
    public void TestParseInteger(string bencodeInput, long expected)
    {
        var bytes = Encoding.UTF8.GetBytes(bencodeInput);
        using var stream = new MemoryStream(bytes);

        var parser = new BencodeParser(stream);
        var actual = parser.Parse();

        Assert.IsType<long>(actual);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GetListTestData))]
    public void TestParseList(string bencodeInput, List<object> expected)
    {
        var bytes = Encoding.UTF8.GetBytes(bencodeInput);
        using var stream = new MemoryStream(bytes);

        var parser = new BencodeParser(stream);
        var actual = parser.Parse();

        Assert.IsType<List<object>>(actual);
        var expectedList = expected.ToList();
        var actualList = (List<object>)actual;

        Assert.Equal(expectedList.Count, actualList.Count);

        AssertBencodeListEqual(expectedList, actualList);
    }

    [Theory]
    [MemberData(nameof(GetDictionaryTestData))]
    public void TestParseDictionary(string bencodeInput, Dictionary<string, object> expected)
    {
        var bytes = Encoding.UTF8.GetBytes(bencodeInput);
        using var stream = new MemoryStream(bytes);

        var parser = new BencodeParser(stream);
        var actual = parser.Parse();

        Assert.IsType<Dictionary<string, object>>(actual);
        var expectedDictionary = expected.ToDictionary();
        var actualDictionary = (Dictionary<string, object>)actual;

        Assert.Equal(expectedDictionary.Count, actualDictionary.Count);

        AssertBencodeDictionaryEqual(expectedDictionary, actualDictionary);
    }

    private void AssertBencodeListEqual(List<object> expected, List<object> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int i = 0; i < expected.Count; i++)
        {
            AssertBencodeEqual(expected[i], actual[i]);
        }
    }

    private void AssertBencodeDictionaryEqual(Dictionary<string, object> expected, Dictionary<string, object> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        foreach (var key in expected.Keys)
        {
            Assert.True(actual.ContainsKey(key), $"Key '{key}' not found in actual dictionary");

            AssertBencodeEqual(expected[key], actual[key]);
        }
    }

    private void AssertBencodeEqual(object expected, object actual)
    {
        switch (expected)
        {
            case Dictionary<string, object> expectedDictionary:
                var actualDictionary = Assert.IsType<Dictionary<string, object>>(actual);
                AssertBencodeDictionaryEqual(expectedDictionary, actualDictionary);
                break;

            case List<object> expectedList:
                var actualList = Assert.IsType<List<object>>(actual);
                AssertBencodeListEqual(expectedList, actualList);
                break;

            case long expectedLong:
                var actualLong = Assert.IsType<long>(actual);
                Assert.Equal(expectedLong, actualLong);
                break;

            case string expectedString:
                Assert.IsType<byte[]> (actual);
                Assert.Equal(expectedString, Encoding.UTF8.GetString((byte[])actual));
                break;

            default:
                throw new ArgumentException($"Unsupported type: {expected.GetType()}");
        }
    }

    public static IEnumerable<object[]> GetStringTestData()
    {
        yield return new object[] { "0:", "" };
        yield return new object[] { "3:123", "123" };
        yield return new object[] { "5:Wait!", "Wait!" };
        yield return new object[] { "29:Til I come back to your side!", "Til I come back to your side!" };
    }

    public static IEnumerable<object[]> GetIntegerTestData()
    {
        yield return new object[] { "i0e", 0L };
        yield return new object[] { "i42e", 42L };
        yield return new object[] { "i-20e", -20L };
        yield return new object[] { "i1234567890e", 1234567890L };
    }

    public static IEnumerable<object[]> GetListTestData()
    {
        yield return new object[] { "le", new List<object> { } };
        yield return new object[] { "li100e2:aae", new List<object> { 100L, "aa" } };
        yield return new object[] { "l0:3:1235:Wait!e", new List<object> { "", "123", "Wait!" } };
        yield return new object[] { "li-20ei123ee", new List<object> { -20L, 123L } };
        yield return new object[] {
            "lleli123ei-123eel4:wait2:hiee",
            new List<object> {
                new List<object> { },
                new List<object> { 123L, -123L },
                new List<object> { "wait", "hi" }
            }
        };
        yield return new object[] {
            "ld3:foo3:bar3:baz3:quxe5:helloee",
            new List<object> {
                new Dictionary<string, object> {
                    ["foo"] = "bar",
                    ["baz"] = "qux"
                },
                "hello"
            }
        };
    }

    public static IEnumerable<object[]> GetDictionaryTestData()
    {
        yield return new object[] { "de", new Dictionary<string, object> { } };

        yield return new object[] {
            "d3:foo3:bare",
            new Dictionary<string, object> {
                ["foo"] = "bar"
            }
        };

        yield return new object[] {
            "d1:a1:x1:b1:y1:c1:ze",
            new Dictionary<string, object> {
                ["a"] = "x",
                ["b"] = "y",
                ["c"] = "z"
            }
        };

        yield return new object[] {
            "d6:numberi123e4:listl1:a1:be5:empty0:e",
            new Dictionary<string, object> {
                ["number"] = 123L,
                ["list"] = new List<object> { "a", "b" },
                ["empty"] = ""
            }
        };

        yield return new object[] {
            "d5:innerd3:foo3:bare9:outer_val5:helloee",
            new Dictionary<string, object> {
                    ["inner"] = new Dictionary<string, object> {
                    ["foo"] = "bar"
                },
                ["outer_val"] = "hello"
            }
        };
    }
}