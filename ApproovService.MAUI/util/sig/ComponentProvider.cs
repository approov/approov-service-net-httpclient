using System.Net.Http;

namespace Approov.Util.Sig;

public interface IComponentProvider
{
    string GetComponentValue(string identifier);
}

public class ApproovHttpMessageComponentProvider : IComponentProvider
{
    private readonly HttpRequestMessage _request;

    public ApproovHttpMessageComponentProvider(HttpRequestMessage request)
        => _request = request;

    public string GetComponentValue(string identifier)
    {
        return identifier switch
        {
            "@method" => _request.Method.Method,
            "@target-uri" => _request.RequestUri?.AbsoluteUri ?? "",
            "@path" => _request.RequestUri?.AbsolutePath ?? "/",
            "@authority" => _request.RequestUri?.Authority ?? "",
            "@scheme" => _request.RequestUri?.Scheme ?? "",
            "@query" => GetQuery(),
            "@request-target" => GetRequestTarget(),
            "@status" => throw new InvalidOperationException(
                "@status is not available on requests"),
            _ when identifier.StartsWith('@') => throw new InvalidOperationException(
                $"Unsupported derived component: {identifier}"),
            _ => GetHeaderValue(identifier)
        };
    }

    private string GetQuery()
    {
        // RFC 9421 §2.2.7: the @query value includes the leading '?', and an absent query
        // is represented by a single '?'. .NET's Uri.Query already retains the leading '?'.
        string q = _request.RequestUri?.Query ?? "";
        return q.Length > 0 ? q : "?";
    }

    private string GetRequestTarget()
    {
        string path = _request.RequestUri?.AbsolutePath ?? "/";
        string query = _request.RequestUri?.Query ?? "";
        return query.Length > 0 ? path + query : path;
    }

    private string GetHeaderValue(string header)
    {
        if (_request.Headers.TryGetValues(header, out var values))
            return string.Join(", ", values);
        if (_request.Content?.Headers.TryGetValues(header, out var contentValues) == true)
            return string.Join(", ", contentValues);
        throw new InvalidOperationException($"Header not found: {header}");
    }
}
