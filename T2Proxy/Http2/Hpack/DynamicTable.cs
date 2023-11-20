
using System;
using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack;

public class DynamicTable
{
    private HttpHeader[] headerFields = Array.Empty<HttpHeader>();
    private int head;
    private int tail;

    public int Capacity { get; private set; } = -1;

    public int Size { get; private set; }

    public DynamicTable(int initialCapacity)
    {
        SetCapacity(initialCapacity);
    }
    public int Length()
    {
        int length;
        if (head < tail)
            length = headerFields.Length - tail + head;
        else
            length = head - tail;

        return length;
    }

    public HttpHeader GetEntry(int index)
    {
        if (index <= 0 || index > Length()) throw new IndexOutOfRangeException();

        var i = head - index;
        if (i < 0) return headerFields[i + headerFields.Length]!;

        return headerFields[i]!;
    }

    public void Add(HttpHeader header)
    {
        var headerSize = header.Size;
        if (headerSize > Capacity)
        {
            Clear();
            return;
        }

        while (Size + headerSize > Capacity) Remove();

        headerFields[head++] = header;
        Size += header.Size;
        if (head == headerFields.Length) head = 0;
    }
    public HttpHeader? Remove()
    {
        var removed = headerFields[tail];
        if (removed == null) return null;

        Size -= removed.Size;
        headerFields[tail++] = null!;
        if (tail == headerFields.Length) tail = 0;

        return removed;
    }
    public void Clear()
    {
        while (tail != head)
        {
            headerFields[tail++] = null!;
            if (tail == headerFields.Length) tail = 0;
        }

        head = 0;
        tail = 0;
        Size = 0;
    }

    public void SetCapacity(int capacity)
    {
        if (capacity < 0) throw new ArgumentException("Illegal Capacity: " + capacity);

        if (Capacity == capacity) return;

        Capacity = capacity;

        if (capacity == 0)
            Clear();
        else
            while (Size > capacity)
                Remove();

        var maxEntries = capacity / HttpHeader.HttpHeaderOverhead;
        if (capacity % HttpHeader.HttpHeaderOverhead != 0) maxEntries++;

        if (headerFields != null && headerFields.Length == maxEntries) return;

        var tmp = new HttpHeader[maxEntries];

        var len = Length();
        var cursor = tail;
        for (var i = 0; i < len; i++)
        {
            var entry = headerFields![cursor++];
            tmp[i] = entry!;
            if (cursor == headerFields.Length) cursor = 0;
        }

        tail = 0;
        head = tail + len;
        headerFields = tmp;
    }
}