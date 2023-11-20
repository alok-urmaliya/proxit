using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;

namespace T2Proxy;

public partial class ProxyServer
{
 
    internal bool ValidateServerCertificate(object sender, SessionEventArgsBase sessionArgs,
        X509Certificate certificate, X509Chain chain,
        SslPolicyErrors sslPolicyErrors)
    {

        if (ServerCertificateValidationCallback != null)
        {
            var args = new CertificateValidationEventArgs(sessionArgs, certificate, chain, sslPolicyErrors);


            ServerCertificateValidationCallback.InvokeAsync(this, args, ExceptionFunc).Wait();
            return args.IsValid;
        }

        if (sslPolicyErrors == SslPolicyErrors.None) return true;

        return false;
    }

    internal X509Certificate? SelectClientCertificate(object sender, SessionEventArgsBase sessionArgs,
        string targetHost,
        X509CertificateCollection localCertificates,
        X509Certificate remoteCertificate, string[] acceptableIssuers)
    {
        X509Certificate? clientCertificate = null;

        if (acceptableIssuers != null && acceptableIssuers.Length > 0 && localCertificates != null &&
            localCertificates.Count > 0)
            foreach (var certificate in localCertificates)
            {
                var issuer = certificate.Issuer;
                if (Array.IndexOf(acceptableIssuers, issuer) != -1) clientCertificate = certificate;
            }


        if (clientCertificate == null
            && localCertificates != null && localCertificates.Count > 0)
            clientCertificate = localCertificates[0];


        if (ClientCertificateSelectionCallback != null)
        {
            var args = new CertificateSelectionEventArgs(sessionArgs, targetHost, localCertificates, remoteCertificate,
                acceptableIssuers)
            {
                ClientCertificate = clientCertificate
            };


            ClientCertificateSelectionCallback.InvokeAsync(this, args, ExceptionFunc).Wait();
            return args.ClientCertificate;
        }

        return clientCertificate;
    }
}