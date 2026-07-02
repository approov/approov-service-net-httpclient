namespace Approov;

public class ApproovRequestMutations
{
    public string? TokenHeaderKey { get; set; }
    public string? TraceIDHeaderKey { get; set; }
    public string? OriginalURL { get; set; }

    private readonly List<string> _substitutionHeaderKeys = new();
    private readonly List<string> _substitutionQueryParamKeys = new();

    public IReadOnlyList<string> SubstitutionHeaderKeys => _substitutionHeaderKeys;
    public IReadOnlyList<string> SubstitutionQueryParamKeys => _substitutionQueryParamKeys;

    public void AddSubstitutionHeaderKey(string key) => _substitutionHeaderKeys.Add(key);
    public void AddSubstitutionQueryParamKey(string key) => _substitutionQueryParamKeys.Add(key);
}
