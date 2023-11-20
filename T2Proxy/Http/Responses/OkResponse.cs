using System.Net;

namespace T2Proxy.Http.Responses;
public sealed class OkResponse : Response
{
    public OkResponse()
    {
        StatusCode = (int)HttpStatusCode.OK;
        StatusDescription = "OK";
    }
    public OkResponse(byte[] body) : this()
    {
        Body = body;
    }
}