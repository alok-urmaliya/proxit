using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.Network.Certificate;
internal interface ICertificateMaker
{
    X509Certificate2 MakeCertificate(string sSubjectCn, X509Certificate2? signingCert);
}