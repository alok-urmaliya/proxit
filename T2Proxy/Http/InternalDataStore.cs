using System.Collections.Generic;

namespace T2Proxy.Http;

internal class InternalDataStore : Dictionary<string, object>
{
    public bool TryGetValueAs<T>(string key, out T value)
    {
        var result = TryGetValue(key, out var value1);
        if (result)
            value = (T)value1;
        else
            
            value = default!;

        return result;
    }

    public T GetAs<T>(string key)
    {
        return (T)this[key];
    }
}