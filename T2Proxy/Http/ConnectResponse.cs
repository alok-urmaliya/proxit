using System;
using System.Net;
using T2Proxy.StreamExtended;

namespace T2Proxy.Http;

public class ConnectResponse : Response
{
    public ServerHelloInfo? ServerHelloInfo { get; set; }

    internal static ConnectResponse CreateSuccessfulConnectResponse(Version httpVersion)
    {
        var response = new ConnectResponse
        {
            HttpVersion = httpVersion,
            StatusCode = (int)HttpStatusCode.OK,
            StatusDescription = "Connection Established"
        };

        return response;
    }
}