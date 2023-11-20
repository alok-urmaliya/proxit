using System;
using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.Network;
internal sealed class CachedCertificate
{
    public CachedCertificate(X509Certificate2 certificate)
    {
        Certificate = certificate;
    }

    internal X509Certificate2 Certificate { get; }

    internal DateTime LastAccess { get; set; }
}