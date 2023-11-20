using System;
using T2Proxy.Models;

namespace T2Proxy.Helpers;

internal struct RequestStatusInfo
{
    public string Method { get; set; }

    public ByteStream RequestUri { get; set; }

    public Version Version { get; set; }

    public bool IsEmpty()
    {
        return Method == null && RequestUri.Length == 0 && Version == null;
    }
}