using System.Text;

namespace Approov.Util.HttpSfv;

public static class SFV
{
    public static string SerializeStringItem(StringItem item)
    {
        var sb = new StringBuilder();
        sb.Append(SerializeBareItem(item.Value));
        foreach (var (key, val) in item.Parameters)
        {
            sb.Append(';');
            sb.Append(key);
            sb.Append('=');
            sb.Append(SerializeBareItem(val));
        }
        return sb.ToString();
    }

    public static string SerializeBareItem(object value) => value switch
    {
        string text => $"\"{EscapeString(text)}\"",
        int integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        long integer => integer.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool boolean => boolean ? "?1" : "?0",
        _ => throw new ArgumentException(
            $"Unsupported Structured Field value type: {value?.GetType().Name ?? "null"}",
            nameof(value))
    };

    private static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c < 0x20 || c > 0x7e)
                throw new ArgumentException(
                    "Structured Field strings must contain printable ASCII characters",
                    nameof(value));
            if (c is '"' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string SerializeInnerList(IReadOnlyList<StringItem> items)
    {
        var sb = new StringBuilder();
        sb.Append('(');
        for (int i = 0; i < items.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(SerializeStringItem(items[i]));
        }
        sb.Append(')');
        return sb.ToString();
    }

    // Serialize as a Byte Sequence member: key=:<base64>:
    public static string SerializeDictionary(string key, byte[] value)
        => $"{key}=:{Convert.ToBase64String(value)}:";

    // Serialize as an Inner List member: key=(items...)
    public static string SerializeDictionary(string key, IReadOnlyList<StringItem> innerList)
        => $"{key}={SerializeInnerList(innerList)}";
}
