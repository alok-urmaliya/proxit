using Microsoft.Extensions.Configuration;
using Org.BouncyCastle.Asn1.Crmf;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using T2Proxy;
using T2Proxy.EventArguments;
using T2Proxy.Exceptions;
using T2Proxy.Helpers;
using T2Proxy.Http;
using T2Proxy.Models;
using T2Proxy.StreamExtended.Network;
using T2Proxy_Console.Helpers;

namespace T2Proxy_Console
{
    public class ProxyController : IDisposable
    {
        private readonly ProxyServer proxyServer;
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ConcurrentQueue<Tuple<ConsoleColor?, string>> consoleMessageQueue = new ConcurrentQueue<Tuple<ConsoleColor?, string>>();
        public List<string> SessionData = new List<string>();

        private ExplicitServerEndPoint explicitEndPoint;
        private IConfiguration _config;
        private string filter;
        public ProxyController(IConfiguration config)
        {
            Task.Run(() => ListenToConsole());

            proxyServer = new ProxyServer();
            proxyServer.TcpTimeWaitSeconds = 10;
            proxyServer.ConnectionTimeOutSeconds = 15;
            proxyServer.ReuseSocket = false;
            proxyServer.EnableConnectionPool = false;
            proxyServer.ForwardToUpstreamGateway = true;
            proxyServer.CertificateManager.SaveFakeCertificates = true;
            _config = config;
            filter = _config["filter"] ?? "google";
        }

        private CancellationToken CancellationToken => cancellationTokenSource.Token;

        /// <summary>
        /// Disposes the resources acquired
        /// </summary>
        public void Dispose()
        {
            cancellationTokenSource.Dispose();
            proxyServer.Dispose();
        }

        /// <summary>
        /// Starts the proxy server
        /// </summary>
        public void StartProxy()
        {
            string filename = "Session_" + DateTime.Now;

            proxyServer.BeforeRequest += OnRequest;
            proxyServer.BeforeResponse += OnResponse;

            proxyServer.ServerCertificateValidationCallback += OnCertificateValidation;
            proxyServer.ClientCertificateSelectionCallback += OnCertificateSelection;

            explicitEndPoint = new ExplicitServerEndPoint(IPAddress.Any, 8888);

            explicitEndPoint.BeforeTunnelConnectRequest += OnBeforeTunnelConnectRequest;
            explicitEndPoint.BeforeTunnelConnectResponse += OnBeforeTunnelConnectResponse;

            proxyServer.AddEndPoint(explicitEndPoint);
            proxyServer.Start();

            foreach (var endPoint in proxyServer.ProxyEndPoints)
                Console.WriteLine("Listening on '{0}' endpoint at Ip {1} and port: {2} ", endPoint.GetType().Name,
                    endPoint.IpAddress, endPoint.Port);

            if (RunTime.IsWindows) proxyServer.SetAsSystemProxy(explicitEndPoint, ServerProtocolType.AllHttp);
        }
        /// <summary>
        /// Stop the proxy server
        /// </summary>
        public void Stop()
        {
            explicitEndPoint.BeforeTunnelConnectRequest -= OnBeforeTunnelConnectRequest;
            explicitEndPoint.BeforeTunnelConnectResponse -= OnBeforeTunnelConnectResponse;

            proxyServer.BeforeRequest -= OnRequest;
            proxyServer.BeforeResponse -= OnResponse;
            proxyServer.ServerCertificateValidationCallback -= OnCertificateValidation;
            proxyServer.ClientCertificateSelectionCallback -= OnCertificateSelection;

            FileHelper.WriteDataToFile(SessionData);
            proxyServer.Stop();
        }

        public async Task OnBeforeTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
        {
            var hostname = e.HttpClient.Request.RequestUri.Host;
            e.GetState().PipelineInfo.AppendLine(nameof(OnBeforeTunnelConnectRequest) + ":" + hostname);
            
            if(hostname.Contains(filter))
            {
                WriteToConsole("Tunnel to: " + hostname, ConsoleColor.Yellow);
            }
            var clientLocalIp = e.ClientLocalEndPoint.Address;
            if (!clientLocalIp.Equals(IPAddress.Loopback) && !clientLocalIp.Equals(IPAddress.IPv6Loopback))
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(clientLocalIp, 0);
        }

        private Task OnBeforeTunnelConnectResponse(object sender, TunnelConnectSessionEventArgs e)
        {
            e.GetState().PipelineInfo.AppendLine(nameof(OnBeforeTunnelConnectResponse) + ":" + e.HttpClient.Request.RequestUri);
            return Task.CompletedTask;
        }

        /// <summary>
        /// To show the user ammount of data sent
        /// </summary>
        private void WebSocket_DataSent(object sender, DataEventArgs e)
        {
            var args = (SessionEventArgs)sender;
            WebSocketDataSentReceived(args, e, true);
        }

        /// <summary>
        /// To show the user ammount of data received
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void WebSocket_DataReceived(object sender, DataEventArgs e)
        {
            var args = (SessionEventArgs)sender;
            WebSocketDataSentReceived(args, e, true);
        }
#pragma warning disable CS0618
        public void WebSocketDataSentReceived(SessionEventArgs args, DataEventArgs e, bool sent)
        {
            foreach (var frame in args.WebSocketDecoder.Decode(e.Buffer, e.Offset, e.Count))
            {
                if (frame.OpCode == WebsocketOpCode.Binary)
                {
                    var data = frame.Data.ToArray();
                    var str = string.Join(",", data.ToArray().Select(x => x.ToString("X2")));
                    WriteToConsole(str);
                }
                if (frame.OpCode == WebsocketOpCode.Text)
                    WriteToConsole(frame.GetText());
            }
        }
#pragma warning restore CS0618
        
        /// <summary>
        /// Handles the request sent event, and prints the data to the console 
        /// </summary>
        /// <returns>async method prints data in the console</returns>
        private async Task OnRequest(object sender, SessionEventArgs e)
        {
            e.GetState().PipelineInfo.AppendLine(nameof(OnRequest) + ":" + e.HttpClient.Request.RequestUri);
            var clientLocalIp = e.ClientLocalEndPoint.Address;
            if (!clientLocalIp.Equals(IPAddress.Loopback) && !clientLocalIp.Equals(IPAddress.IPv6Loopback))
                e.HttpClient.UpStreamEndPoint = new IPEndPoint(clientLocalIp, 0);
            
            if (e.HttpClient.Request.HasBody)
            {
                e.HttpClient.Request.KeepBody = true;
                await e.GetRequestBody();
            }
            const int truncateLimit = 512;
            var session = e.HttpClient;

            var request = session.Request;
            var fullData = (request.IsBodyRead ? request.Body : null) ?? Array.Empty<byte>();
            var data = fullData;

            bool truncated = data.Length > truncateLimit;
            if (truncated)
            {
                data = data.Take(truncateLimit).ToArray();
            }

            var reqOutput = new StringBuilder();
            reqOutput.AppendLine("URL : " + request.Url);
            reqOutput.AppendLine(request.HeaderText);
            reqOutput.AppendLine(request.Encoding.GetString(data));

            if (truncated)
            {
                reqOutput.AppendLine();
                reqOutput.AppendLine($"data is truncated after {truncateLimit} bytes");
            }

            if(request.Url.Contains(filter))
            {
                Console.WriteLine("Session Item:");
                WriteToConsole("Active Client Connections: " + ((ProxyServer)sender).ClientConnectionCount);
                WriteToConsole(reqOutput.ToString());

                SessionData.Add(reqOutput.ToString());
            }
        }

        /// <summary>
        /// Handles the event of incoming response
        /// </summary>
        /// <returns>async method prints response data in the console</returns>
        private async Task OnResponse(object sender, SessionEventArgs e)
        {
            e.GetState().PipelineInfo.AppendLine(nameof(OnResponse));
            if (e.HttpClient.ConnectRequest?.TunnelType == TunnelType.Websocket)
            {
                e.DataSent += WebSocket_DataSent;
                e.DataReceived += WebSocket_DataReceived;
            }

            const int truncateLimit = 512;

            var session = e.HttpClient;
            var response = session.Response;
            if (response.HasBody)
            {
                response.KeepBody = true;
                await e.GetResponseBody();
            }

            var fullData = (response.IsBodyRead ? response.Body : null) ?? Array.Empty<byte>();
            var data = fullData;

            var truncated = data.Length > truncateLimit;
            if (truncated)
            {
                data = data.Take(truncateLimit).ToArray();
            }

            var resOutput = new StringBuilder();
            resOutput.AppendLine(response.HeaderText);
            resOutput.AppendLine(response.Encoding.GetString(data));
            if (truncated)
            {
                resOutput.AppendLine();
                resOutput.AppendLine($"data was truncated after {truncateLimit} bytes");
            }

            if(e.HttpClient.Request.Url.Contains(filter))
            {
                WriteToConsole("Active Client Connections: " + ((ProxyServer)sender).ServerConnectionCount);
                WriteToConsole(resOutput.ToString(), ConsoleColor.Blue);

                SessionData.Add(resOutput.ToString());
            }
        }
        public Task OnCertificateValidation(object sender, CertificateValidationEventArgs e)
        {
            e.GetState().PipelineInfo.AppendLine(nameof(OnCertificateValidation));
            if (e.SslPolicyErrors == SslPolicyErrors.None) e.IsValid = true;

            return Task.CompletedTask;
        }
        public Task OnCertificateSelection(object sender, CertificateSelectionEventArgs e)
        {
            e.GetState().PipelineInfo.AppendLine(nameof(OnCertificateSelection));
            return Task.CompletedTask;
        }

        /// <summary>
        /// prints data to the console, gives the developer ability to specify the color
        /// </summary>
        /// <param name="message">text that has to be printed</param>
        /// <param name="consoleColor">text color</param>
        private void WriteToConsole(string message, ConsoleColor? consoleColor = null)
        {
            consoleMessageQueue.Enqueue(new Tuple<ConsoleColor?, string>(consoleColor, message));
        }

        private async Task ListenToConsole()
        {
            while (!CancellationToken.IsCancellationRequested)
            {
                while (consoleMessageQueue.TryDequeue(out var item))
                {
                    var consoleColor = item.Item1;
                    var message = item.Item2;

                    if (consoleColor.HasValue)
                    {
                        var existing = Console.ForegroundColor;
                        Console.ForegroundColor = consoleColor.Value;
                        Console.WriteLine(message);
                        Console.ForegroundColor = existing;
                    }
                    else
                    {
                        Console.WriteLine(message);
                    }
                }
            }
            await Task.Delay(50);
        }
    }
}