using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;
using T2Proxy.Helpers;
using T2Proxy.Helpers.WinHttp;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.Network;
using T2Proxy.Network.Tcp;
using T2Proxy.StreamExtended.BufferPool;

namespace T2Proxy
{

    public partial class ProxyServer : IDisposable
    {
        internal static readonly string UriSchemeHttp = Uri.UriSchemeHttp;

        internal static readonly string UriSchemeHttps = Uri.UriSchemeHttps;

        internal static ByteStream UriSchemeHttp8 = (ByteStream)UriSchemeHttp;
        internal static ByteStream UriSchemeHttps8 = (ByteStream)UriSchemeHttps;

        private int clientConnectionCount;

        private ExceptionHandler? exceptionFunc;

        private int serverConnectionCount;

        private WinHttpWebProxyFinder? systemProxyResolver;

        public ProxyServer(bool userTrustRootCertificate = true, bool machineTrustRootCertificate = false,
            bool trustRootCertificateAsAdmin = false) : this(null, null, userTrustRootCertificate,
            machineTrustRootCertificate, trustRootCertificateAsAdmin)
        {
        }

        public ProxyServer(string? rootCertificateName, string? rootCertificateIssuerName,
            bool userTrustRootCertificate = true, bool machineTrustRootCertificate = false,
            bool trustRootCertificateAsAdmin = false)
        {
            BufferPool = new DefaultBufferPool();
            ProxyEndPoints = new List<ServerEndPoint>();
            TcpConnectionFactory = new TcpConnectionFactory(this);
            if (RunTime.IsWindows && !RunTime.IsUwpOnWindows) SystemProxySettingsManager = new SystemProxyManager();

            CertificateManager = new CertificateManager(rootCertificateName, rootCertificateIssuerName,
                userTrustRootCertificate, machineTrustRootCertificate, trustRootCertificateAsAdmin, ExceptionFunc);
        }

        private TcpConnectionFactory TcpConnectionFactory { get; }

        private SystemProxyManager? SystemProxySettingsManager { get; }

        public int NetworkFailureRetryAttempts { get; set; } = 1;

        public bool ProxyRunning { get; private set; }

        public bool ForwardToUpstreamGateway { get; set; }

        public Uri UpstreamProxyConfigurationScript { get; set; }

        public bool EnableWinAuth { get; set; }

        public bool EnableHttp2 { get; set; } = false;

        public X509RevocationMode CheckCertificateRevocation { get; set; }

        public bool Enable100ContinueBehaviour { get; set; }

        public bool EnableConnectionPool { get; set; } = false;

        public bool EnableTcpServerConnectionPrefetch { get; set; } = true;

        public bool NoDelay { get; set; } = true;

        public int ConnectionTimeOutSeconds { get; set; } = 60;

        public int ConnectTimeOutSeconds { get; set; } = 20;

        public int MaxCachedConnections { get; set; } = 4;

        public int TcpTimeWaitSeconds { get; set; } = 30;

        public bool ReuseSocket { get; set; } = true;

        public int ClientConnectionCount => clientConnectionCount;

        public int ServerConnectionCount => serverConnectionCount;

        public string ProxyAuthenticationRealm { get; set; } = "T2Proxy";

#pragma warning disable 618
        public SslProtocols SupportedSslProtocols { get; set; } =
            SslProtocols.Ssl3 | SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12
#if NET6_0_OR_GREATER
        | SslProtocols.Tls13
#endif
        ;
#pragma warning restore 618

#pragma warning disable 618
        public SslProtocols SupportedServerSslProtocols { get; set; } = SslProtocols.None;
#pragma warning restore 618

        public IBufferPool BufferPool { get; set; }

        public CertificateManager CertificateManager { get; }

        public IExternalServer? UpStreamHttpProxy { get; set; }

        public IExternalServer? UpStreamHttpsProxy { get; set; }

        public IPEndPoint? UpStreamEndPoint { get; set; }

        public List<ServerEndPoint> ProxyEndPoints { get; set; }

        public Func<SessionEventArgsBase, Task<IExternalServer?>>? GetCustomUpStreamProxyFunc { get; set; }

        public Func<SessionEventArgsBase, Task<IExternalServer?>>? CustomUpStreamProxyFailureFunc { get; set; }

        public ExceptionHandler? ExceptionFunc
        {
            get => exceptionFunc;
            set
            {
                exceptionFunc = value;
                CertificateManager.ExceptionFunc = value;
            }
        }

        public Func<SessionEventArgsBase, string, string, Task<bool>>? ProxyBasicAuthenticateFunc { get; set; }

        public Func<SessionEventArgsBase, string, string, Task<ServerAuthenticationContext>>? ProxySchemeAuthenticateFunc
        {
            get;
            set;
        }

        public IEnumerable<string> ProxyAuthenticationSchemes { get; set; } = new string[0];

        public event EventHandler? ClientConnectionCountChanged;

        public event EventHandler? ServerConnectionCountChanged;

        public event AsyncEventHandler<CertificateValidationEventArgs>? ServerCertificateValidationCallback;

        public event AsyncEventHandler<CertificateSelectionEventArgs>? ClientCertificateSelectionCallback;

        public event AsyncEventHandler<SessionEventArgs>? BeforeRequest;

#if DEBUG
        public event AsyncEventHandler<BeforeBodyWriteEventArgs>? OnRequestBodyWrite;
#endif
        public event AsyncEventHandler<SessionEventArgs>? BeforeResponse;

#if DEBUG
        public event AsyncEventHandler<BeforeBodyWriteEventArgs>? OnResponseBodyWrite;
#endif
        public event AsyncEventHandler<SessionEventArgs>? AfterResponse;

        public event AsyncEventHandler<Socket>? OnClientConnectionCreate;

        public event AsyncEventHandler<Socket>? OnServerConnectionCreate;

        public event AsyncEventHandler<ConnectRequest>? BeforeUpStreamConnectRequest;

        public int ThreadPoolWorkerThread { get; set; } = Environment.ProcessorCount;

        public void AddEndPoint(ServerEndPoint endPoint)
        {
            if (ProxyEndPoints.Any(x =>
                    x.IpAddress.Equals(endPoint.IpAddress) && endPoint.Port != 0 && x.Port == endPoint.Port))
                throw new Exception("Cannot add another endpoint to same port & ip address");

            ProxyEndPoints.Add(endPoint);

            if (ProxyRunning) Listen(endPoint);
        }

        public void RemoveEndPoint(ServerEndPoint endPoint)
        {
            if (ProxyEndPoints.Contains(endPoint) == false)
                throw new Exception("Cannot remove endPoints not added to proxy");

            ProxyEndPoints.Remove(endPoint);

            if (ProxyRunning) QuitListen(endPoint);
        }

        public void SetAsSystemHttpProxy(ExplicitServerEndPoint endPoint)
        {
            SetAsSystemProxy(endPoint, ServerProtocolType.Http);
        }

        public void SetAsSystemHttpsProxy(ExplicitServerEndPoint endPoint)
        {
            SetAsSystemProxy(endPoint, ServerProtocolType.Https);
        }

        public void SetAsSystemProxy(ExplicitServerEndPoint endPoint, ServerProtocolType protocolType)
        {
            if (SystemProxySettingsManager == null)
                throw new NotSupportedException(@"Setting system proxy settings are only supported in Windows.
                            Please manually configure you operating system to use this proxy's port and address.");

            ValidateEndPointAsSystemProxy(endPoint);

            var isHttp = (protocolType & ServerProtocolType.Http) > 0;
            var isHttps = (protocolType & ServerProtocolType.Https) > 0;

            if (isHttps)
            {
                CertificateManager.EnsureRootCertificate();

                if (!CertificateManager.CertValidated)
                {
                    protocolType = protocolType & ~ServerProtocolType.Https;
                    isHttps = false;
                }
            }

            if (isHttp) ProxyEndPoints.OfType<ExplicitServerEndPoint>().ToList().ForEach(x => x.IsSystemHttpProxy = false);

            if (isHttps) ProxyEndPoints.OfType<ExplicitServerEndPoint>().ToList().ForEach(x => x.IsSystemHttpsProxy = false);

            SystemProxySettingsManager.SetProxy(
                Equals(endPoint.IpAddress, IPAddress.Any) |
                Equals(endPoint.IpAddress, IPAddress.Loopback)
                    ? "localhost"
                    : endPoint.IpAddress.ToString(),
                endPoint.Port,
                protocolType);

            if (isHttp) endPoint.IsSystemHttpProxy = true;

            if (isHttps) endPoint.IsSystemHttpsProxy = true;

            string? proxyType = null;
            switch (protocolType)
            {
                case ServerProtocolType.Http:
                    proxyType = "HTTP";
                    break;
                case ServerProtocolType.Https:
                    proxyType = "HTTPS";
                    break;
                case ServerProtocolType.AllHttp:
                    proxyType = "HTTP and HTTPS";
                    break;
            }

            if (protocolType != ServerProtocolType.None)
                Console.WriteLine("Set endpoint at Ip {0} and port: {1} as System {2} Proxy", endPoint.IpAddress,
                    endPoint.Port, proxyType);
        }

        public void DisableSystemHttpProxy()
        {
            DisableSystemProxy(ServerProtocolType.Http);
        }

        public void DisableSystemHttpsProxy()
        {
            DisableSystemProxy(ServerProtocolType.Https);
        }

        public void RestoreOriginalProxySettings()
        {
            if (SystemProxySettingsManager == null)
                throw new NotSupportedException(@"Setting system proxy settings are only supported in Windows.
                            Please manually configure your operating system to use this proxy's port and address.");

            SystemProxySettingsManager.RestoreOriginalSettings();
        }
        public void DisableSystemProxy(ServerProtocolType protocolType)
        {
            if (SystemProxySettingsManager == null)
                throw new NotSupportedException(@"Setting system proxy settings are only supported in Windows.
                            Please manually configure your operating system to use this proxy's port and address.");

            SystemProxySettingsManager.RemoveProxy(protocolType);
        }

        public void DisableAllSystemProxies()
        {
            if (SystemProxySettingsManager == null)
                throw new NotSupportedException(@"Setting system proxy settings are only supported in Windows.
                            Please manually confugure you operating system to use this proxy's port and address.");

            SystemProxySettingsManager.DisableAllProxy();
        }

        public void Start(bool changeSystemProxySettings = true)
        {
            if (ProxyRunning) throw new Exception("Proxy is already running.");

            SetThreadPoolMinThread(ThreadPoolWorkerThread);

            if (ProxyEndPoints.OfType<ExplicitServerEndPoint>().Any(x => x.GenericCertificate == null))
                CertificateManager.EnsureRootCertificate();

            if (changeSystemProxySettings && SystemProxySettingsManager != null && RunTime.IsWindows &&
                !RunTime.IsUwpOnWindows)
            {
                var proxyInfo = SystemProxySettingsManager.GetProxyInfoFromRegistry();
                if (proxyInfo?.Proxies != null)
                {
                    var protocolToRemove = ServerProtocolType.None;
                    foreach (var proxy in proxyInfo.Proxies.Values)
                        if (NetworkHelper.IsLocalIpAddress(proxy.HostName)
                            && ProxyEndPoints.Any(x => x.Port == proxy.Port))
                            protocolToRemove |= proxy.ProtocolType;

                    if (protocolToRemove != ServerProtocolType.None)
                        SystemProxySettingsManager.RemoveProxy(protocolToRemove, false);
                }
            }

            if (ForwardToUpstreamGateway && GetCustomUpStreamProxyFunc == null && SystemProxySettingsManager != null)
            {
                systemProxyResolver = new WinHttpWebProxyFinder();
                if (UpstreamProxyConfigurationScript != null)
                    systemProxyResolver.UsePacFile(UpstreamProxyConfigurationScript);
                else
                    systemProxyResolver.LoadFromIe();

                GetCustomUpStreamProxyFunc = GetSystemUpStreamProxy;
            }

            ProxyRunning = true;

            CertificateManager.ClearIdleCertificates();

            foreach (var endPoint in ProxyEndPoints) Listen(endPoint);
        }
        public void Stop()
        {
            if (!ProxyRunning) throw new Exception("Proxy is not running.");

            if (SystemProxySettingsManager != null)
            {
                var setAsSystemProxy = ProxyEndPoints.OfType<ExplicitServerEndPoint>()
                    .Any(x => x.IsSystemHttpProxy || x.IsSystemHttpsProxy);

                if (setAsSystemProxy) SystemProxySettingsManager.RestoreOriginalSettings();
            }

            foreach (var endPoint in ProxyEndPoints) QuitListen(endPoint);

            ProxyEndPoints.Clear();

            CertificateManager?.StopClearIdleCertificates();
            TcpConnectionFactory.Dispose();

            ProxyRunning = false;
        }
        private void Listen(ServerEndPoint endPoint)
        {
            endPoint.Listener = new TcpListener(endPoint.IpAddress, endPoint.Port);

            if (ReuseSocket && RunTime.IsSocketReuseAvailable())
                endPoint.Listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            try
            {
                endPoint.Listener.Start();

                endPoint.Port = ((IPEndPoint)endPoint.Listener.LocalEndpoint).Port;

                endPoint.Listener.BeginAcceptSocket(OnAcceptConnection, endPoint);
            }
            catch (SocketException ex)
            {
                var pex = new Exception(
                    $"Endpoint {endPoint} failed to start. Check inner exception and exception data for details.", ex);
                pex.Data.Add("ipAddress", endPoint.IpAddress);
                pex.Data.Add("port", endPoint.Port);
                throw pex;
            }
        }
        private void ValidateEndPointAsSystemProxy(ExplicitServerEndPoint endPoint)
        {
            if (endPoint == null) throw new ArgumentNullException(nameof(endPoint));

            if (!ProxyEndPoints.Contains(endPoint))
                throw new Exception("Cannot set endPoints not added to proxy as system proxy");

            if (!ProxyRunning) throw new Exception("Cannot set system proxy settings before proxy has been started.");
        }

        private Task<IExternalServer?> GetSystemUpStreamProxy(SessionEventArgsBase sessionEventArgs)
        {
            var proxy = systemProxyResolver!.GetProxy(sessionEventArgs.HttpClient.Request.RequestUri);
            return Task.FromResult(proxy);
        }
        private void OnAcceptConnection(IAsyncResult asyn)
        {
            var endPoint = (ServerEndPoint)asyn.AsyncState;

            Socket? tcpClient = null;

            try
            {
                tcpClient = endPoint.Listener!.EndAcceptSocket(asyn);
                tcpClient.NoDelay = NoDelay;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
            }

            if (tcpClient != null)
                Task.Run(async () => { await HandleClient(tcpClient, endPoint); });

            try
            {
                endPoint.Listener!.BeginAcceptSocket(OnAcceptConnection, endPoint);
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
            {
            }
        }


        private void SetThreadPoolMinThread(int workerThreads)
        {
            ThreadPool.GetMinThreads(out var minWorkerThreads, out var minCompletionPortThreads);
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out _);

            minWorkerThreads = Math.Min(maxWorkerThreads, Math.Max(workerThreads, Environment.ProcessorCount));

            ThreadPool.SetMinThreads(minWorkerThreads, minCompletionPortThreads);
        }


        private async Task HandleClient(Socket tcpClientSocket, ServerEndPoint endPoint)
        {
            tcpClientSocket.ReceiveTimeout = ConnectionTimeOutSeconds * 1000;
            tcpClientSocket.SendTimeout = ConnectionTimeOutSeconds * 1000;

            tcpClientSocket.LingerState = new LingerOption(true, TcpTimeWaitSeconds);

            await InvokeClientConnectionCreateEvent(tcpClientSocket);

            using (var clientConnection = new TcpClientConnection(this, tcpClientSocket))
            {
                if (endPoint is ExplicitServerEndPoint eep)
                    await HandleClient(eep, clientConnection);
                else if (endPoint is TransparentServerEndPoint tep)
                    await HandleClient(tep, clientConnection);
                else if (endPoint is SocksServerEndPoint sep) await HandleClient(sep, clientConnection);
            }
        }

        private void OnException(HttpClientStream? clientStream, Exception exception)
        {
            ExceptionFunc?.Invoke(exception);
        }

        
        private void QuitListen(ServerEndPoint endPoint)
        {
            endPoint.Listener!.Stop();
            endPoint.Listener.Server.Dispose();
        }

        internal void UpdateClientConnectionCount(bool increment)
        {
            if (increment)
                Interlocked.Increment(ref clientConnectionCount);
            else
                Interlocked.Decrement(ref clientConnectionCount);

            try
            {
                ClientConnectionCountChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnException(null, ex);
            }
        }

    
        internal void UpdateServerConnectionCount(bool increment)
        {
            if (increment)
                Interlocked.Increment(ref serverConnectionCount);
            else
                Interlocked.Decrement(ref serverConnectionCount);

            try
            {
                ServerConnectionCountChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnException(null, ex);
            }
        }

        internal async Task InvokeClientConnectionCreateEvent(Socket clientSocket)
        {          
            if (OnClientConnectionCreate != null)
                await OnClientConnectionCreate.InvokeAsync(this, clientSocket, ExceptionFunc);
        }

        internal async Task InvokeServerConnectionCreateEvent(Socket serverSocket)
        {
            if (OnServerConnectionCreate != null)
                await OnServerConnectionCreate.InvokeAsync(this, serverSocket, ExceptionFunc);
        }

        private RetryPolicy<T> RetryPolicy<T>() where T : Exception
        {
            return new RetryPolicy<T>(NetworkFailureRetryAttempts, TcpConnectionFactory);
        }

        private bool disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            disposed = true;

            if (ProxyRunning)
                try
                {
                    Stop();
                }
                catch
                {
                    // ignore
                }

            if (disposing)
            {
                CertificateManager?.Dispose();
                BufferPool?.Dispose();
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~ProxyServer()
        {
            Dispose(false);
        }
    }
}