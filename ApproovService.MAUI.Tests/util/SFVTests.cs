using Approov.Util.HttpSfv;
using Xunit;

namespace Approov.Tests;

public class SFVTests
{
    // --- StringItem ---

    [Fact]
    public void StringItem_NoParams_SerializesAsQuotedString()
    {
        var item = new StringItem("hello");
        Assert.Equal("\"hello\"", SFV.SerializeStringItem(item));
    }

    [Fact]
    public void StringItem_WithParams_SerializesWithSemicolon()
    {
        var item = new StringItem("hello",
            new[] { ("req", "true") });
        Assert.Equal("\"hello\";req=\"true\"", SFV.SerializeStringItem(item));
    }

    [Fact]
    public void StringItem_MultipleParams_AllAppended()
    {
        var item = new StringItem("@method",
            new[] { ("key", "sig1"), ("created", "1735000000") });
        Assert.Equal("\"@method\";key=\"sig1\";created=\"1735000000\"", SFV.SerializeStringItem(item));
    }

    // --- SerializeInnerList ---

    [Fact]
    public void SerializeInnerList_EmptyList_ReturnsBrackets()
    {
        Assert.Equal("()", SFV.SerializeInnerList(Array.Empty<StringItem>()));
    }

    [Fact]
    public void SerializeInnerList_TwoItems_SpaceSeparated()
    {
        var items = new[] { new StringItem("@method"), new StringItem("@path") };
        Assert.Equal("(\"@method\" \"@path\")", SFV.SerializeInnerList(items));
    }

    [Fact]
    public void SerializeInnerList_ItemsWithParams_IncludesParams()
    {
        var items = new[]
        {
            new StringItem("@signature-params", new[] { ("key", "sig1") }),
            new StringItem("@path")
        };
        Assert.Equal("(\"@signature-params\";key=\"sig1\" \"@path\")",
            SFV.SerializeInnerList(items));
    }

    [Fact]
    public void StringItem_QuotesAndBackslashes_AreEscaped()
    {
        var item = new StringItem("a\"b\\c", new[] { ("name", "d\"e\\f") });

        Assert.Equal("\"a\\\"b\\\\c\";name=\"d\\\"e\\\\f\"",
            SFV.SerializeStringItem(item));
    }

    // --- SerializeDictionary(key, byte[]) ---

    [Fact]
    public void SerializeDictionary_ByteArray_ProducesBase64Item()
    {
        byte[] bytes = new byte[] { 0x01, 0x02, 0x03 };
        string expected = $"sig1=:{Convert.ToBase64String(bytes)}:";
        Assert.Equal(expected, SFV.SerializeDictionary("sig1", bytes));
    }

    // --- SerializeDictionary(key, InnerList) ---

    [Fact]
    public void SerializeDictionary_InnerList_ProducesKeyEqualsParens()
    {
        var items = new[] { new StringItem("@method"), new StringItem("@path") };
        Assert.Equal("sig-params=(\"@method\" \"@path\")",
            SFV.SerializeDictionary("sig-params", (IReadOnlyList<StringItem>)items));
    }
}
