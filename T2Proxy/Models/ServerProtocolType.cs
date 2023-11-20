using System;

namespace T2Proxy.Models;

[Flags]
public enum ServerProtocolType
{
  
    None = 0,

    Http = 1,

    Https = 2,

    AllHttp = Http | Https
}