using System;
using System.ComponentModel;
using T2Proxy.Exceptions;
using T2Proxy.Extensions;
using T2Proxy.Models;

namespace T2Proxy.Http;

[TypeConverter(typeof(ExpandableObjectConverter))]
public class Response : RequestResponseBase
{
    public Response()
    {
    }
    public Response(byte[] body)
    {
        Body = body;
    }

    public int StatusCode { get; set; }

    public string StatusDescription { get; set; } = string.Empty;

    internal string RequestMethod { get; set; }

    public override bool HasBody
    {
        get
        {
            if (RequestMethod == "HEAD") return false;

            var contentLength = ContentLength;

            if (contentLength == 0) return false;

            if (IsChunked || contentLength > 0 || !KeepAlive) return true;

            if (ContentLength == -1 && HttpVersion == HttpHeader.Version20) return true;
            if (KeepAlive && HttpVersion == HttpHeader.Version10) return true;

            return false;
        }
    }

    public bool KeepAlive
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.Connection);

            if (headerValue != null)
                if (headerValue.EqualsIgnoreCase(KnownHeaders.ConnectionClose.String))
                    return false;

            return true;
        }
    }

    public override string HeaderText
    {
        get
        {
            var headerBuilder = new HeaderBuilder();
            headerBuilder.WriteResponseLine(HttpVersion, StatusCode, StatusDescription);
            headerBuilder.WriteHeaders(Headers);
            return headerBuilder.GetString(HttpHeader.Encoding);
        }
    }

    internal override void EnsureBodyAvailable(bool throwWhenNotReadYet = true)
    {
        if (BodyInternal != null) return;

        if (!HasBody) throw new BodyNotFoundException("Response don't have a body.");

        if (!IsBodyRead && throwWhenNotReadYet)
            throw new Exception("Response body is not read yet. " +
                                "Use SessionEventArgs.GetResponseBody() or SessionEventArgs.GetResponseBodyAsString() " +
                                "method to read the response body.");
    }

    internal static void ParseResponseLine(string httpStatus, out Version version, out int statusCode,
        out string statusDescription)
    {
        var firstSpace = httpStatus.IndexOf(' ');
        if (firstSpace == -1) throw new Exception("Invalid HTTP status line: " + httpStatus);

        var httpVersion = httpStatus.AsSpan(0, firstSpace);

        version = HttpHeader.Version11;
        if (httpVersion.EqualsIgnoreCase("HTTP/1.0".AsSpan())) version = HttpHeader.Version10;

        var secondSpace = httpStatus.IndexOf(' ', firstSpace + 1);
        if (secondSpace != -1)
        {
#if NET6_0_OR_GREATER
            statusCode = int.Parse(httpStatus.AsSpan(firstSpace + 1, secondSpace - firstSpace - 1));
#else
            statusCode = int.Parse(httpStatus.AsSpan(firstSpace + 1, secondSpace - firstSpace - 1).ToString());
#endif
            statusDescription = httpStatus.AsSpan(secondSpace + 1).ToString();
        }
        else
        {
#if NET6_0_OR_GREATER
            statusCode = int.Parse(httpStatus.AsSpan(firstSpace + 1));
#else
            statusCode = int.Parse(httpStatus.AsSpan(firstSpace + 1).ToString());
#endif
            statusDescription = string.Empty;
        }
    }
}