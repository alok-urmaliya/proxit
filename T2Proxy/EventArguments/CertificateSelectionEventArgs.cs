using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.EventArguments;

public class CertificateSelectionEventArgs : ServerEventArgsBase
{
    public CertificateSelectionEventArgs(SessionEventArgsBase session, string targetHost,
        X509CertificateCollection localCertificates, X509Certificate remoteCertificate, string[] acceptableIssuers) :
        base(session.Server, session.ClientConnection)
    {
        Session = session;
        TargetHost = targetHost;
        LocalCertificates = localCertificates;
        RemoteCertificate = remoteCertificate;
        AcceptableIssuers = acceptableIssuers;
    }

    public SessionEventArgsBase Session { get; }

    public string TargetHost { get; }

    public X509CertificateCollection LocalCertificates { get; }

    public X509Certificate RemoteCertificate { get; }

    public string[] AcceptableIssuers { get; }

    public X509Certificate? ClientCertificate { get; set; }
}