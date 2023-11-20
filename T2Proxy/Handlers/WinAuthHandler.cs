using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Network.WinAuth;
using T2Proxy.Network.WinAuth.Security;

namespace T2Proxy;

public partial class ProxyServer
{
    private static readonly HashSet<string> authHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "WWW-Authenticate",

        "WWWAuthenticate",
        "NTLMAuthorization",
        "NegotiateAuthorization",
        "KerberosAuthorization"
    };
    
    private static readonly HashSet<string> authSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NTLM",
        "Negotiate",
        "Kerberos"
    };
    
    private async Task Handle401UnAuthorized(SessionEventArgs args)
    {
        string? headerName = null;
        HttpHeader? authHeader = null;

        var response = args.HttpClient.Response;

        var header = response.Headers.NonUniqueHeaders.FirstOrDefault(x => authHeaderNames.Contains(x.Key));

        if (!header.Equals(new KeyValuePair<string, List<HttpHeader>>())) headerName = header.Key;

        if (headerName != null)
            authHeader = response.Headers.NonUniqueHeaders[headerName]
                .FirstOrDefault(
                    x => authSchemes.Any(y => x.Value.StartsWith(y, StringComparison.OrdinalIgnoreCase)));

        if (authHeader == null)
        {
            headerName = null;

            var uHeader = response.Headers.Headers.FirstOrDefault(x => authHeaderNames.Contains(x.Key));

            if (!uHeader.Equals(new KeyValuePair<string, HttpHeader>())) headerName = uHeader.Key;

            if (headerName != null)
                authHeader = authSchemes.Any(x => response.Headers.Headers[headerName].Value
                    .StartsWith(x, StringComparison.OrdinalIgnoreCase))
                    ? response.Headers.Headers[headerName]
                    : null;
        }

        if (authHeader != null)
        {
            var scheme = authSchemes.Contains(authHeader.Value) ? authHeader.Value : null;

            var expectedAuthState =
                scheme == null ? State.WinAuthState.InitialToken : State.WinAuthState.Unauthorized;

            if (!WinAuthEndPoint.ValidateWinAuthState(args.HttpClient.Data, expectedAuthState))
            {
                await RewriteUnauthorizedResponse(args);
                return;
            }

            var request = args.HttpClient.Request;

            request.Headers.RemoveHeader(KnownHeaders.Authorization);

            if (scheme != null)
            {
                var clientToken = WinAuthHandler.GetInitialAuthToken(request.Host!, scheme, args.HttpClient.Data);

                var auth = string.Concat(scheme, clientToken);

                request.Headers.SetOrAddHeaderValue(KnownHeaders.Authorization, auth);

                if (request.HasBody) request.ContentLength = 0;
            }
            else
            {
                scheme = authSchemes.First(x =>
                    authHeader.Value.StartsWith(x, StringComparison.OrdinalIgnoreCase) &&
                    authHeader.Value.Length > x.Length + 1);

                var serverToken = authHeader.Value.Substring(scheme.Length + 1);
                var clientToken = WinAuthHandler.GetFinalAuthToken(request.Host!, serverToken, args.HttpClient.Data);

                var auth = string.Concat(scheme, clientToken);

                request.Headers.SetOrAddHeaderValue(KnownHeaders.Authorization, auth);

                if (request.OriginalHasBody) request.ContentLength = request.Body.Length;

                args.HttpClient.Connection.IsWinAuthenticated = true;
            }


            args.ReRequest = true;
        }
    }

    private async Task RewriteUnauthorizedResponse(SessionEventArgs args)
    {
        var response = args.HttpClient.Response;

        foreach (var authHeaderName in authHeaderNames) response.Headers.RemoveHeader(authHeaderName);

        var authErrorMessage =
            "<div class=\"inserted-by-proxy\"><h2>NTLM authentication through T2Proxy (" +
            args.ClientLocalEndPoint +
            ") failed. Please check credentials.</h2></div>";
        var originalErrorMessage =
            "<div class=\"inserted-by-proxy\"><h3>Response from remote web server below.</h3></div><br/>";
        var body = await args.GetResponseBodyAsString(args.CancellationTokenSource.Token);
        var idx = body.IndexOfIgnoreCase("<body>");
        if (idx >= 0)
        {
            var bodyPos = idx + "<body>".Length;
            body = body.Insert(bodyPos, authErrorMessage + originalErrorMessage);
        }
        else
        {
            body =
                "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">" +
                "<html xmlns=\"http://www.w3.org/1999/xhtml\">" +
                "<body>" +
                authErrorMessage +
                "</body>" +
                "</html>";
        }

        args.SetResponseBodyString(body);
    }
}