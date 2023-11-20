using System.Net;

namespace T2Proxy.Http.Responses;


public sealed class RedirectResponse : Response
{
    public RedirectResponse()
    {
        StatusCode = (int)HttpStatusCode.Found;
        StatusDescription = "Found";
    }
}