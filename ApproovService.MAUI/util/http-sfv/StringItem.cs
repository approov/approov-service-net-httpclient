namespace Approov.Util.HttpSfv;

public readonly struct StringItem
{
    public string Value { get; }
    public IReadOnlyList<(string Key, string Value)> Parameters { get; }

    public StringItem(string value)
    {
        Value = value;
        Parameters = Array.Empty<(string, string)>();
    }

    public StringItem(string value, IEnumerable<(string Key, string Value)> parameters)
    {
        Value = value;
        Parameters = parameters.ToList().AsReadOnly();
    }
}
