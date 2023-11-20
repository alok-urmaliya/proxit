using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.Network;

public interface ICertificateCache
{
    X509Certificate2? LoadRootCertificate(string pathOrName, string password, X509KeyStorageFlags storageFlags);

    void SaveRootCertificate(string pathOrName, string password, X509Certificate2 certificate);

    X509Certificate2? LoadCertificate(string subjectName, X509KeyStorageFlags storageFlags);

    void SaveCertificate(string subjectName, X509Certificate2 certificate);

    void Clear();
}