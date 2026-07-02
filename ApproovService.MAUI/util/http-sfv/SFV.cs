using System.Text;

namespace Approov.Util.HttpSfv;

public static class SFV
{
    public static string SerializeStringItem(StringItem item)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        sb.Append(item.Value);
        sb.Append('"');
        foreach (var (key, val) in item.Parameters)
        {
            sb.Append(';');
            sb.Append(key);
            sb.Append("=\"");
            sb.Append(val);
            sb.Append('"');
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
