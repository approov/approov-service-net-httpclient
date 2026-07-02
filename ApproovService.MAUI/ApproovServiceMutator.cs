// ApproovService.MAUI/ApproovServiceMutator.cs
using System.Net.Http;
using System.Text.RegularExpressions;

namespace Approov;

public interface IApproovServiceMutator
{
    void HandlePrecheckResult(IApproovTokenFetchResult result);
    void HandleFetchTokenResult(IApproovTokenFetchResult result);
    void HandleFetchSecureStringResult(IApproovTokenFetchResult result, string operation, string key);
    void HandleFetchCustomJWTResult(IApproovTokenFetchResult result);
    bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request);
    bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult result, string url);
    bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult result, string header);
    bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult result, string queryKey);
    HttpRequestMessage HandleInterceptorProcessedRequest(HttpRequestMessage request,
                                                         ApproovRequestMutations changes);
    bool HandlePinningShouldProcessRequest(HttpRequestMessage request);
}

public sealed class ApproovServiceMutatorDefault : IApproovServiceMutator
{
    public static readonly ApproovServiceMutatorDefault Shared = new();
    private ApproovServiceMutatorDefault() { }

    public void HandlePrecheckResult(IApproovTokenFetchResult r)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException("precheck: rejected", r.ARC, r.RejectionReasons);
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException("precheck network error: " + r.Status);
            case ApproovTokenFetchStatus.Success:
            case ApproovTokenFetchStatus.UnknownKey:
                return;
            default:
                throw new PermanentException("precheck: " + r.Status);
        }
    }

    public void HandleFetchTokenResult(IApproovTokenFetchResult r)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Success:
                return;
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException("fetchToken network error: " + r.Status);
            default:
                throw new PermanentException("fetchToken: " + r.Status);
        }
    }

    public void HandleFetchSecureStringResult(IApproovTokenFetchResult r, string operation, string key)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException($"fetchSecureString {operation} for {key}: rejected",
                    r.ARC, r.RejectionReasons);
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException($"fetchSecureString {operation} for {key}: " + r.Status);
            case ApproovTokenFetchStatus.Success:
            case ApproovTokenFetchStatus.UnknownKey:
                return;
            default:
                throw new PermanentException($"fetchSecureString {operation} for {key}: " + r.Status);
        }
    }

    public void HandleFetchCustomJWTResult(IApproovTokenFetchResult r)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException("fetchCustomJWT: rejected", r.ARC, r.RejectionReasons);
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException("fetchCustomJWT network error: " + r.Status);
            case ApproovTokenFetchStatus.Success:
                return;
            default:
                throw new PermanentException("fetchCustomJWT: " + r.Status);
        }
    }

    public bool HandleInterceptorShouldProcessRequest(HttpRequestMessage request)
    {
        string urlString = request.RequestUri?.AbsoluteUri ?? "";
        foreach (var (_, regex) in ApproovService.GetExclusionURLRegexs())
        {
            if (regex.IsMatch(urlString)) return false;
        }
        return true;
    }

    public bool HandleInterceptorFetchTokenResult(IApproovTokenFetchResult r, string url)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Success:
                return true;
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException($"token fetch for {url}: " + r.Status);
            case ApproovTokenFetchStatus.NoApproovService:
            case ApproovTokenFetchStatus.UnknownUrl:
            case ApproovTokenFetchStatus.UnprotectedUrl:
                return false;
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException($"token fetch for {url}: rejected", r.ARC, r.RejectionReasons);
            default:
                throw new PermanentException($"token fetch for {url}: " + r.Status);
        }
    }

    public bool HandleInterceptorHeaderSubstitutionResult(IApproovTokenFetchResult r, string header)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Success:
                return true;
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException($"header substitution for {header}: rejected",
                    r.ARC, r.RejectionReasons);
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException($"header substitution for {header}: " + r.Status);
            case ApproovTokenFetchStatus.UnknownKey:
                return false;
            default:
                throw new PermanentException($"header substitution for {header}: " + r.Status);
        }
    }

    public bool HandleInterceptorQueryParamSubstitutionResult(IApproovTokenFetchResult r, string queryKey)
    {
        switch (r.Status)
        {
            case ApproovTokenFetchStatus.Success:
                return true;
            case ApproovTokenFetchStatus.Rejected:
                throw new RejectionException($"query param substitution for {queryKey}: rejected",
                    r.ARC, r.RejectionReasons);
            case ApproovTokenFetchStatus.NoNetwork:
            case ApproovTokenFetchStatus.PoorNetwork:
            case ApproovTokenFetchStatus.MitmDetected:
                throw new NetworkingErrorException($"query param substitution for {queryKey}: " + r.Status);
            case ApproovTokenFetchStatus.UnknownKey:
                return false;
            default:
                throw new PermanentException($"query param substitution for {queryKey}: " + r.Status);
        }
    }

    public HttpRequestMessage HandleInterceptorProcessedRequest(HttpRequestMessage request,
                                                                ApproovRequestMutations changes)
        => request;

    public bool HandlePinningShouldProcessRequest(HttpRequestMessage request) => true;
}
