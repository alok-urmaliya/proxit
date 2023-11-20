using System;
using System.Net;
using System.Text;
using T2Proxy.Extensions;
using T2Proxy.Http;

namespace T2Proxy.Models;

public class HttpHeader
{
    public const int HttpHeaderOverhead = 32;

#if NET6_0_OR_GREATER
    internal static Version VersionUnknown => HttpVersion.Unknown;
#else
    internal static Version VersionUnknown { get; } = new(0, 0);
#endif

    internal static Version Version10 => HttpVersion.Version10;

    internal static Version Version11 => HttpVersion.Version11;

#if NET6_0_OR_GREATER
    internal static Version Version20 => HttpVersion.Version20;
#else
    internal static Version Version20 { get; } = new(2, 0);
#endif

    internal static readonly Encoding DefaultEncoding = Encoding.GetEncoding("ISO-8859-1");

    public static Encoding Encoding => DefaultEncoding;

    internal static readonly HttpHeader ProxyConnectionKeepAlive = new("Proxy-Connection", "keep-alive");

    private string? nameString;

    private string? valueString;

    public HttpHeader(string name, string value)
    {
        if (string.IsNullOrEmpty(name)) throw new Exception("Name cannot be null or empty");

        nameString = name.Trim();
        NameData = nameString.GetByteString();

        valueString = value.Trim();
        ValueData = valueString.GetByteString();
    }

    internal HttpHeader(KnownHeader name, string value)
    {
        nameString = name.String;
        NameData = name.String8;

        valueString = value.Trim();
        ValueData = valueString.GetByteString();
    }

    internal HttpHeader(KnownHeader name, KnownHeader value)
    {
        nameString = name.String;
        NameData = name.String8;

        valueString = value.String;
        ValueData = value.String8;
    }

    internal HttpHeader(ByteStream name, ByteStream value)
    {
        if (name.Length == 0) throw new Exception("Name cannot be empty");

        NameData = name;
        ValueData = value;
    }

    private protected HttpHeader(ByteStream name, ByteStream value, bool headerEntry)
    {
        NameData = name;
        ValueData = value;
    }

    public string Name => nameString ??= NameData.GetString();

    internal ByteStream NameData { get; }

    public string Value => valueString ??= ValueData.GetString();

    internal ByteStream ValueData { get; private set; }

    public int Size => Name.Length + Value.Length + HttpHeaderOverhead;

    internal static int SizeOf(ByteStream name, ByteStream value)
    {
        return name.Length + value.Length + HttpHeaderOverhead;
    }

    internal void SetValue(string value)
    {
        valueString = value;
        ValueData = value.GetByteString();
    }

    internal void SetValue(KnownHeader value)
    {
        valueString = value.String;
        ValueData = value.String8;
    }

    public override string ToString()
    {
        return $"{Name}: {Value}";
    }

    internal static HttpHeader GetProxyAuthorizationHeader(string? userName, string? password)
    {
        var result = new HttpHeader(KnownHeaders.ProxyAuthorization,
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userName}:{password}")));
        return result;
    }
}