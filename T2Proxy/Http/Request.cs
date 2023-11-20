using System;
using System.ComponentModel;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Models;

namespace T2Proxy.Http;

[TypeConverter(typeof(ExpandableObjectConverter))]
public class Request : RequestResponseBase
{
    private ByteStream requestUriString8;

    public string Method { get; set; }

    public bool IsHttps { get; internal set; }

    internal ByteStream RequestUriString8
    {
        get => requestUriString8;
        set
        {
            requestUriString8 = value;
            var scheme = URIExtensions.GetScheme(value);
            if (scheme.Length > 0) IsHttps = scheme.Equals(ProxyServer.UriSchemeHttps8);
        }
    }

    internal ByteStream Authority { get; set; }

    public Uri RequestUri
    {
        get
        {
            var url = Url;
            try
            {
                return new Uri(url);
            }
            catch (Exception ex)
            {
                throw new Exception($"Invalid URI: '{url}'", ex);
            }
        }
        set => Url = value.OriginalString;
    }

    public string Url
    {
        get
        {
            var url = RequestUriString8.GetString();
            if (URIExtensions.GetScheme(RequestUriString8).Length == 0)
            {
                var hostAndPath = Host ?? Authority.GetString();

                if (url.StartsWith("/"))
                {
                    hostAndPath += url;
                }

                url = string.Concat(IsHttps ? "https://" : "http://", hostAndPath);
            }

            return url;
        }
        set => RequestUriString = value;
    }
    public string RequestUriString
    {
        get => RequestUriString8.GetString();
        set
        {
            RequestUriString8 = (ByteStream)value;

            var scheme = URIExtensions.GetScheme(RequestUriString8);
            if (scheme.Length > 0 && Host != null)
            {
                var uri = new Uri(value);
                Host = uri.Authority;
                Authority = ByteStream.Empty;
            }
        }
    }
    public override bool HasBody
    {
        get
        {
            var contentLength = ContentLength;
            if (contentLength == 0) return false;

            if (IsChunked || contentLength > 0) return true;

            if (Method == "POST" && HttpVersion == HttpHeader.Version10) return true;

            return false;
        }
    }

    public string? Host
    {
        get => Headers.GetHeaderValueOrNull(KnownHeaders.Host);
        set => Headers.SetOrAddHeaderValue(KnownHeaders.Host, value);
    }
    public bool ExpectContinue
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Expect);
            return KnownHeaders.Expect100Continue.Equals(headerValue);
        }
    }

    public bool IsMultipartFormData => ContentType?.StartsWith("multipart/form-data") == true;

    internal bool CancelRequest { get; set; }

    public bool UpgradeToWebSocket
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Upgrade);

            if (headerValue == null) return false;

            return headerValue.EqualsIgnoreCase(KnownHeaders.UpgradeWebsocket.String);
        }
    }
    public bool ExpectationSucceeded { get; internal set; }

    public bool ExpectationFailed { get; internal set; }

    public override string HeaderText
    {
        get
        {
            var headerBuilder = new HeaderBuilder();
            headerBuilder.WriteRequestLine(Method, RequestUriString, HttpVersion);
            headerBuilder.WriteHeaders(Headers);
            return headerBuilder.GetString(HttpHeader.Encoding);
        }
    }

    internal override void EnsureBodyAvailable(bool throwWhenNotReadYet = true)
    {
        if (BodyInternal != null) return;

        if (!HasBody)
            throw new BodyNotFoundException("Request don't have a body. " +
                                            "Please verify that this request is a Http POST/PUT/PATCH and request " +
                                            "content length is greater than zero before accessing the body.");

        if (!IsBodyRead)
        {
            if (Locked) throw new Exception("You cannot get the request body after request is made to server.");

            if (throwWhenNotReadYet)
                throw new Exception("Request body is not read yet. " +
                                    "Use SessionEventArgs.GetRequestBody() or SessionEventArgs.GetRequestBodyAsString() " +
                                    "method to read the request body.");
        }
    }

    internal static void ParseRequestLine(string httpCmd, out string method, out ByteStream requestUri,
        out Version version)
    {
        var firstSpace = httpCmd.IndexOf(' ');
        if (firstSpace == -1)
            throw new Exception("Invalid HTTP request line: " + httpCmd);

        var lastSpace = httpCmd.LastIndexOf(' ');
        method = httpCmd.Substring(0, firstSpace);
        if (!IsAllUpper(method)) method = method.ToUpper();

        version = HttpHeader.Version11;

        if (firstSpace == lastSpace)
        {
            requestUri = (ByteStream)httpCmd.AsSpan(firstSpace + 1).ToString();
        }
        else
        {
            requestUri = (ByteStream)httpCmd.AsSpan(firstSpace + 1, lastSpace - firstSpace - 1).ToString();

            var httpVersion = httpCmd.AsSpan(lastSpace + 1);

            if (httpVersion.EqualsIgnoreCase("HTTP/1.0".AsSpan(0))) version = HttpHeader.Version10;
        }
    }

    private static bool IsAllUpper(string input)
    {
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (ch < 'A' || ch > 'Z') return false;
        }

        return true;
    }
}