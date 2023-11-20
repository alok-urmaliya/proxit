using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using T2Proxy.Models;

namespace T2Proxy.Http;

[TypeConverter(typeof(ExpandableObjectConverter))]
public class HeaderCollection : IEnumerable<HttpHeader>
{
    private readonly Dictionary<string, HttpHeader> headers;

    private readonly Dictionary<string, List<HttpHeader>> nonUniqueHeaders;

    public HeaderCollection()
    {
        headers = new Dictionary<string, HttpHeader>(StringComparer.OrdinalIgnoreCase);
        nonUniqueHeaders = new Dictionary<string, List<HttpHeader>>(StringComparer.OrdinalIgnoreCase);
        Headers = new ReadOnlyDictionary<string, HttpHeader>(headers);
        NonUniqueHeaders = new ReadOnlyDictionary<string, List<HttpHeader>>(nonUniqueHeaders);
    }

    public ReadOnlyDictionary<string, HttpHeader> Headers { get; }

    public ReadOnlyDictionary<string, List<HttpHeader>> NonUniqueHeaders { get; }

    public IEnumerator<HttpHeader> GetEnumerator()
    {
        return headers.Values.Concat(nonUniqueHeaders.Values.SelectMany(x => x)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public bool HeaderExists(string name)
    {
        return headers.ContainsKey(name) || nonUniqueHeaders.ContainsKey(name);
    }

    public List<HttpHeader>? GetHeaders(string name)
    {
        if (headers.ContainsKey(name))
            return new List<HttpHeader>
            {
                headers[name]
            };

        if (nonUniqueHeaders.ContainsKey(name)) return new List<HttpHeader>(nonUniqueHeaders[name]);

        return null;
    }

    public HttpHeader? GetFirstHeader(string name)
    {
        if (headers.TryGetValue(name, out var header)) return header;

        if (nonUniqueHeaders.TryGetValue(name, out var h)) return h.FirstOrDefault();

        return null;
    }

    internal HttpHeader? GetFirstHeader(KnownHeader name)
    {
        if (headers.TryGetValue(name.String, out var header)) return header;

        if (nonUniqueHeaders.TryGetValue(name.String, out var h)) return h.FirstOrDefault();

        return null;
    }

    public List<HttpHeader> GetAllHeaders()
    {
        var result = new List<HttpHeader>();

        result.AddRange(headers.Select(x => x.Value));
        result.AddRange(nonUniqueHeaders.SelectMany(x => x.Value));

        return result;
    }

    public void AddHeader(string name, string value)
    {
        AddHeader(new HttpHeader(name, value));
    }

    internal void AddHeader(KnownHeader name, string value)
    {
        AddHeader(new HttpHeader(name, value));
    }

    internal void AddHeader(KnownHeader name, KnownHeader value)
    {
        AddHeader(new HttpHeader(name, value));
    }

    public void AddHeader(HttpHeader newHeader)
    {
        if (nonUniqueHeaders.TryGetValue(newHeader.Name, out var list))
        {
            list.Add(newHeader);
            return;
        }

        if (headers.TryGetValue(newHeader.Name, out var existing))
        {
            headers.Remove(newHeader.Name);

            nonUniqueHeaders.Add(newHeader.Name, new List<HttpHeader>
            {
                existing,
                newHeader
            });
        }
        else
        {
            headers.Add(newHeader.Name, newHeader);
        }
    }
    public void AddHeaders(IEnumerable<HttpHeader>? newHeaders)
    {
        if (newHeaders == null) return;

        foreach (var header in newHeaders) AddHeader(header);
    }
    public void AddHeaders(IEnumerable<KeyValuePair<string, string>> newHeaders)
    {
        if (newHeaders == null) return;

        foreach (var header in newHeaders) AddHeader(header.Key, header.Value);
    }
    public void AddHeaders(IEnumerable<KeyValuePair<string, HttpHeader>>? newHeaders)
    {
        if (newHeaders == null) return;

        foreach (var header in newHeaders)
        {
            if (header.Key != header.Value.Name)
                throw new Exception(
                    "Header name mismatch. Key and the name of the HttpHeader object should be the same.");

            AddHeader(header.Value);
        }
    }
    public bool RemoveHeader(string headerName)
    {
        var result = headers.Remove(headerName);

        if (nonUniqueHeaders.Remove(headerName)) result = true;

        return result;
    }
    public bool RemoveHeader(KnownHeader headerName)
    {
        var result = headers.Remove(headerName.String);

        if (nonUniqueHeaders.Remove(headerName.String)) result = true;

        return result;
    }
    public bool RemoveHeader(HttpHeader header)
    {
        if (headers.ContainsKey(header.Name))
        {
            if (headers[header.Name].Equals(header))
            {
                headers.Remove(header.Name);
                return true;
            }
        }
        else if (nonUniqueHeaders.ContainsKey(header.Name))
        {
            if (nonUniqueHeaders[header.Name].RemoveAll(x => x.Equals(header)) > 0) return true;
        }

        return false;
    }

    public void Clear()
    {
        headers.Clear();
        nonUniqueHeaders.Clear();
    }

    internal string? GetHeaderValueOrNull(KnownHeader headerName)
    {
        if (headers.TryGetValue(headerName.String, out var header)) return header.Value;

        return null;
    }

    internal void SetOrAddHeaderValue(KnownHeader headerName, string? value)
    {
        if (value == null)
        {
            RemoveHeader(headerName);
            return;
        }

        if (headers.TryGetValue(headerName.String, out var header))
            header.SetValue(value);
        else
            headers.Add(headerName.String, new HttpHeader(headerName, value));
    }

    internal void SetOrAddHeaderValue(KnownHeader headerName, KnownHeader value)
    {
        if (headers.TryGetValue(headerName.String, out var header))
            header.SetValue(value);
        else
            headers.Add(headerName.String, new HttpHeader(headerName, value));
    }

    internal void FixProxyHeaders()
    {

        var proxyHeader = GetHeaderValueOrNull(KnownHeaders.ProxyConnection);
        RemoveHeader(KnownHeaders.ProxyConnection);

        if (proxyHeader != null) SetOrAddHeaderValue(KnownHeaders.Connection, proxyHeader);
    }
}