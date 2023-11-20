

using T2Proxy.Models;

namespace T2Proxy.Http2.Hpack;

internal interface IHeaderListener
{
    void AddHeader(ByteStream name, ByteStream value, bool sensitive);
}