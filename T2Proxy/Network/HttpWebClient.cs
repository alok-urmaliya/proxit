using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.Extensions;
using T2Proxy.Models;
using T2Proxy.Network.Tcp;

namespace T2Proxy.Http;

public class HttpWebClient
{
    private TcpServerConnection? connection;

    internal HttpWebClient(ConnectRequest? connectRequest, Request request, Lazy<int> processIdFunc)
    {
        ConnectRequest = connectRequest;
        Request = request;
        Response = new Response();
        ProcessId = processIdFunc;
    }

    internal TcpServerConnection Connection
    {
        get
        {
            if (connection == null) throw new Exception("Connection is null");

            return connection;
        }
    }

    internal bool HasConnection => connection != null;

    internal bool CloseServerConnection { get; set; }

    internal InternalDataStore Data { get; } = new();

    public object? UserData { get; set; }

    public IPEndPoint? UpStreamEndPoint { get; set; }

    public ConnectRequest? ConnectRequest { get; }

    public Request Request { get; }

    public Response Response { get; internal set; }

    public Lazy<int> ProcessId { get; internal set; }

    public bool IsHttps => Request.IsHttps;

    internal void SetConnection(TcpServerConnection serverConnection)
    {
        serverConnection.LastAccess = DateTime.UtcNow;
        connection = serverConnection;
    }
    internal async Task SendRequest(bool enable100ContinueBehaviour, bool isTransparent,
        CancellationToken cancellationToken)
    {
        var upstreamProxy = Connection.UpStreamProxy;

        var useUpstreamProxy = upstreamProxy != null && upstreamProxy.ProxyType == ExternalProxyType.Http &&
                               !Connection.IsHttps;

        var serverStream = Connection.Stream;

        string? upstreamProxyUserName = null;
        string? upstreamProxyPassword = null;

        string url;
        if (isTransparent)
        {
            url = Request.RequestUriString;
        }
        else if (!useUpstreamProxy)
        {
            if (URIExtensions.GetScheme(Request.RequestUriString8).Length == 0)
                url = Request.RequestUriString;
            else
                url = Request.RequestUri.GetOriginalPathAndQuery();
        }
        else
        {
            url = Request.RequestUri.ToString();

            if (!string.IsNullOrEmpty(upstreamProxy!.UserName) && upstreamProxy.Password != null)
            {
                upstreamProxyUserName = upstreamProxy.UserName;
                upstreamProxyPassword = upstreamProxy.Password;
            }
        }

        if (url == string.Empty) url = "/";

        var headerBuilder = new HeaderBuilder();
        headerBuilder.WriteRequestLine(Request.Method, url, Request.HttpVersion);
        headerBuilder.WriteHeaders(Request.Headers, !isTransparent, upstreamProxyUserName, upstreamProxyPassword);

        await serverStream.WriteHeadersAsync(headerBuilder, cancellationToken);

        if (enable100ContinueBehaviour && Request.ExpectContinue)
        {
            await ReceiveResponse(cancellationToken);

            if (Response.StatusCode == (int)HttpStatusCode.Continue)
                Request.ExpectationSucceeded = true;
            else
                Request.ExpectationFailed = true;
        }
    }

    internal async Task ReceiveResponse(CancellationToken cancellationToken)
    {
        if (Response.StatusCode != 0) return;

        Response.RequestMethod = Request.Method;

        var httpStatus = await Connection.Stream.ReadResponseStatus(cancellationToken);
        Response.HttpVersion = httpStatus.Version;
        Response.StatusCode = httpStatus.StatusCode;
        Response.StatusDescription = httpStatus.Description;
        await HeaderParser.ReadHeaders(Connection.Stream, Response.Headers, cancellationToken);
    }

    internal void FinishSession()
    {
        connection = null;

        ConnectRequest?.FinishSession();
        Request?.FinishSession();
        Response?.FinishSession();

        Data.Clear();
        UserData = null;
    }
}