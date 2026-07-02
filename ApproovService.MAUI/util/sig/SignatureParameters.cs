using Approov.Util.HttpSfv;

namespace Approov.Util.Sig;

public class SignatureParameters
{
    private readonly List<StringItem> _componentIdentifiers = new();
    private readonly List<(string Key, object Value)> _parameters = new();

    public void AddComponentIdentifier(StringItem item) => _componentIdentifiers.Add(item);

    public void AddParameter(string key, object value) => _parameters.Add((key, value));

    public object? GetParameterValue(string key)
    {
        foreach (var (k, v) in _parameters)
            if (k == key) return v;
        return null;
    }

    public IReadOnlyList<StringItem> ToComponentValue() => _componentIdentifiers.AsReadOnly();

    public IReadOnlyList<(string Key, object Value)> GetParameters() => _parameters.AsReadOnly();
}
