using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace T2Proxy.EventArguments;

public class CertificateValidationEventArgs : ServerEventArgsBase
{
    public CertificateValidationEventArgs(SessionEventArgsBase session, X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors) : base(session.Server, session.ClientConnection)
    {
        Session = session;
        Certificate = certificate;
        Chain = chain;
        SslPolicyErrors = sslPolicyErrors;
    }

    public SessionEventArgsBase Session { get; }

    public X509Certificate Certificate { get; }

    public X509Chain Chain { get; }

    public SslPolicyErrors SslPolicyErrors { get; }

    public bool IsValid { get; set; }
}