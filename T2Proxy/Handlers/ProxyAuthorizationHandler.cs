using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Http;
using T2Proxy.Models;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task<bool> CheckAuthorization(SessionEventArgsBase session)
    {
        if (ProxyBasicAuthenticateFunc == null && ProxySchemeAuthenticateFunc == null) return true;

        var httpHeaders = session.HttpClient.Request.Headers;

        try
        {
            var headerObj = httpHeaders.GetFirstHeader(KnownHeaders.ProxyAuthorization);
            if (headerObj == null)
            {
                session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Required");
                return false;
            }

            var header = headerObj.Value;
            var firstSpace = header.IndexOf(' ');

            if (firstSpace == -1 || header.IndexOf(' ', firstSpace + 1) != -1)
            {
                session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Invalid");
                return false;
            }

            var authenticationType = header.AsMemory(0, firstSpace);
            var credentials = header.AsMemory(firstSpace + 1);

            if (ProxyBasicAuthenticateFunc != null)
                return await AuthenticateUserBasic(session, authenticationType, credentials,
                    ProxyBasicAuthenticateFunc);

            if (ProxySchemeAuthenticateFunc != null)
            {
                var result =
                    await ProxySchemeAuthenticateFunc(session, authenticationType.ToString(), credentials.ToString());

                if (result.Result == ServerAuthenticationResult.ContinuationNeeded)
                {
                    session.HttpClient.Response =
                        CreateAuthentication407Response("Proxy Authentication Invalid", result.Continuation);

                    return false;
                }

                return result.Result == ServerAuthenticationResult.Success;
            }

            return false;
        }
        catch (Exception e)
        {
            OnException(null, new ServerAuthorizationException ("Error whilst authorizing request", session, e,
                httpHeaders));
            session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Invalid");
            return false;
        }
    }

    private async Task<bool> AuthenticateUserBasic(SessionEventArgsBase session,
        ReadOnlyMemory<char> authenticationType, ReadOnlyMemory<char> credentials,
        Func<SessionEventArgsBase, string, string, Task<bool>> proxyBasicAuthenticateFunc)
    {
        if (!KnownHeaders.ProxyAuthorizationBasic.Equals(authenticationType.Span))
        {
         
            session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Invalid");
            return false;
        }

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(credentials.ToString()));
        var colonIndex = decoded.IndexOf(':');
        if (colonIndex == -1)
        {
          
            session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Invalid");
            return false;
        }

        var username = decoded.Substring(0, colonIndex);
        var password = decoded.Substring(colonIndex + 1);
        var authenticated = await proxyBasicAuthenticateFunc(session, username, password);
        if (!authenticated)
            session.HttpClient.Response = CreateAuthentication407Response("Proxy Authentication Invalid");

        return authenticated;
    }

    private Response CreateAuthentication407Response(string description, string? continuation = null)
    {
        var response = new Response
        {
            HttpVersion = HttpHeader.Version11,
            StatusCode = (int)HttpStatusCode.ProxyAuthenticationRequired,
            StatusDescription = description
        };

        if (!string.IsNullOrWhiteSpace(continuation)) return CreateContinuationResponse(response, continuation!);

        if (ProxyBasicAuthenticateFunc != null)
            response.Headers.AddHeader(KnownHeaders.ProxyAuthenticate, $"Basic realm=\"{ProxyAuthenticationRealm}\"");

        if (ProxySchemeAuthenticateFunc != null)
            foreach (var scheme in ProxyAuthenticationSchemes)
                response.Headers.AddHeader(KnownHeaders.ProxyAuthenticate, scheme);

        response.Headers.AddHeader(KnownHeaders.ProxyConnection, KnownHeaders.ProxyConnectionClose);

        response.Headers.FixProxyHeaders();
        return response;
    }

    private Response CreateContinuationResponse(Response response, string continuation)
    {
        response.Headers.AddHeader(KnownHeaders.ProxyAuthenticate, continuation);

        response.Headers.AddHeader(KnownHeaders.ProxyConnection, KnownHeaders.ConnectionKeepAlive);

        response.Headers.FixProxyHeaders();

        return response;
    }
}