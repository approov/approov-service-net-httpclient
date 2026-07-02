using System.Net.Http;

namespace Approov;

public class ApproovHttpClient : HttpClient
{
    public ApproovHttpClient() : base(new ApproovMessageHandler()) { }
    public ApproovHttpClient(ApproovMessageHandler handler) : base(handler) { }
}
