using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using T2Proxy.Compression;
using T2Proxy.Extensions;
using T2Proxy.Helpers;
using T2Proxy.Models;

namespace T2Proxy.Http;

public abstract class RequestResponseBase
{
    private string? bodyString;

    internal Task? Http2BeforeHandlerTask;

    internal MemoryStream? Http2BodyData;

    internal bool Http2IgnoreBodyFrames;

    internal long? Priority;

    internal TaskCompletionSource<bool>? ReadHttp2BeforeHandlerTaskCompletionSource;

    internal TaskCompletionSource<bool>? ReadHttp2BodyTaskCompletionSource;

    protected byte[]? BodyInternal { get; private set; }

    internal bool OriginalHasBody { get; set; }

    internal long OriginalContentLength { get; set; }
    internal bool OriginalIsChunked { get; set; }
    internal string? OriginalContentEncoding { get; set; }

    public bool KeepBody { get; set; }

    public Version HttpVersion { get; set; } = HttpHeader.VersionUnknown;

    public HeaderCollection Headers { get; } = new();

    public long ContentLength
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.ContentLength);

            if (headerValue == null) return -1;

            if (long.TryParse(headerValue, out var contentLen) && contentLen >= 0) return contentLen;

            return -1;
        }

        set
        {
            if (value >= 0)
            {
                Headers.SetOrAddHeaderValue(
                    HttpVersion >= HttpHeader.Version20
                        ? KnownHeaders.ContentLengthHttp2
                        : KnownHeaders.ContentLength, value.ToString());
                IsChunked = false;
            }
            else
            {
                Headers.RemoveHeader(KnownHeaders.ContentLength);
            }
        }
    }

    public string? ContentEncoding => Headers.GetHeaderValueOrNull(KnownHeaders.ContentEncoding)?.Trim();

    public Encoding Encoding => HttpHelper.GetEncodingFromContentType(ContentType);
    public string? ContentType
    {
        get => Headers.GetHeaderValueOrNull(KnownHeaders.ContentType);
        set => Headers.SetOrAddHeaderValue(KnownHeaders.ContentType, value);
    }
    public bool IsChunked
    {
        get
        {
            var headerValue = Headers.GetHeaderValueOrNull(KnownHeaders.TransferEncoding);
            return headerValue != null && headerValue.ContainsIgnoreCase(KnownHeaders.TransferEncodingChunked.String);
        }

        set
        {
            if (value)
            {
                Headers.SetOrAddHeaderValue(KnownHeaders.TransferEncoding, KnownHeaders.TransferEncodingChunked);
                ContentLength = -1;
            }
            else
            {
                Headers.RemoveHeader(KnownHeaders.TransferEncoding);
            }
        }
    }
    public abstract string HeaderText { get; }
    [Browsable(false)]
    public byte[] Body
    {
        get
        {
            EnsureBodyAvailable();
            return BodyInternal!;
        }

        internal set
        {
            BodyInternal = value;
            bodyString = null;

            UpdateContentLength();
        }
    }
    public abstract bool HasBody { get; }
    [Browsable(false)]
    public string BodyString => bodyString ??= Encoding.GetString(Body);

    public bool IsBodyRead { get; internal set; }

    internal bool Locked { get; set; }

    internal bool BodyAvailable => BodyInternal != null;

    internal bool IsBodyReceived { get; set; }

    internal bool IsBodySent { get; set; }

    internal abstract void EnsureBodyAvailable(bool throwWhenNotReadYet = true);

    internal byte[] GetCompressedBody(HttpCompression encodingType, byte[] body)
    {
        using (var ms = new MemoryStream())
        {
            using (var zip = CompressionFactory.Create(encodingType, ms))
            {
                zip.Write(body, 0, body.Length);
            }

            return ms.ToArray();
        }
    }

    internal byte[]? CompressBodyAndUpdateContentLength()
    {
        if (!IsBodyRead && BodyInternal == null) return null;

        var isChunked = IsChunked;
        var contentEncoding = ContentEncoding;

        if (HasBody)
        {
            var body = Body;
            if (contentEncoding != null && body != null)
            {
                body = GetCompressedBody(CompressionUtil.CompressionNameToEnum(contentEncoding), body);

                if (isChunked == false)
                    ContentLength = body.Length;
                else
                    ContentLength = -1;
            }

            return body;
        }

        ContentLength = 0;
        return null;
    }

    internal void UpdateContentLength()
    {
        ContentLength = IsChunked ? -1 : BodyInternal?.Length ?? 0;
    }
    internal void SetOriginalHeaders()
    {
        OriginalHasBody = HasBody;
        OriginalContentLength = ContentLength;
        OriginalIsChunked = IsChunked;
        OriginalContentEncoding = ContentEncoding;
    }
    internal void SetOriginalHeaders(RequestResponseBase requestResponseBase)
    {
        OriginalHasBody = requestResponseBase.OriginalHasBody;
        OriginalContentLength = requestResponseBase.OriginalContentLength;
        OriginalIsChunked = requestResponseBase.OriginalIsChunked;
        OriginalContentEncoding = requestResponseBase.OriginalContentEncoding;
    }
    internal void FinishSession()
    {
        if (!KeepBody)
        {
            BodyInternal = null;
            bodyString = null;
        }
    }

    public override string ToString()
    {
        return HeaderText;
    }
}