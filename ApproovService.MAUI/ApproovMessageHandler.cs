using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace Approov;

public class ApproovMessageHandler : DelegatingHandler
{
    public ApproovMessageHandler() : base(CreatePlatformHandler()) { }
    public ApproovMessageHandler(HttpMessageHandler inner) : base(inner) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ApproovService.IsBodyDigestEnabled())
        {
            var digestOutcome = await BodyDigest.TryAddAsync(request);
            if (digestOutcome == BodyDigestOutcome.CannotDigest
                && ApproovService.IsBodyDigestRequired())
                throw new PermanentException(
                    "Content-Digest is required but the request body cannot be digested");
        }

        var response = ApproovService.UpdateRequestWithApproov(request);

        switch (response.Decision)
        {
            case ApproovFetchDecision.ShouldIgnore:
            case ApproovFetchDecision.ShouldProceed:
                return await base.SendAsync(response.Request!, cancellationToken);

            case ApproovFetchDecision.ShouldRetry:
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    ReasonPhrase = response.SdkMessage
                };

            case ApproovFetchDecision.ShouldFail:
            default:
                if (response.Error != null) throw response.Error;
                throw new ApproovException(response.SdkMessage ?? "Approov request rejected");
        }
    }

    private static HttpMessageHandler CreatePlatformHandler()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback =
            (message, cert, chain, errors) =>
            {
                if (cert == null) return false;
                using var x509 = new X509Certificate2(cert.RawData);
                return ApproovService.VerifyPinning(message, x509);
            };
        return handler;
    }
}
