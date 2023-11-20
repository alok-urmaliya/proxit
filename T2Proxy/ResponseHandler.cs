using System;
using System.Net;
using System.Threading.Tasks;
using T2Proxy.EventArguments;
using T2Proxy.Extensions;
using T2Proxy.Network.WinAuth.Security;

namespace T2Proxy;

public partial class ProxyServer
{
    private async Task HandleHttpSessionResponse(SessionEventArgs args)
    {
        var cancellationToken = args.CancellationTokenSource.Token;

        await args.HttpClient.ReceiveResponse(cancellationToken);
        if (args.HttpClient.Response.StatusCode == (int)HttpStatusCode.Continue)
        {
            await args.ClearResponse(cancellationToken);
            await args.HttpClient.ReceiveResponse(cancellationToken);
        }

        args.TimeLine["Response Received"] = DateTime.UtcNow;

        var response = args.HttpClient.Response;
        args.ReRequest = false;

        if (args.EnableWinAuth)
        {
            if (response.StatusCode == (int)HttpStatusCode.Unauthorized)
                await Handle401UnAuthorized(args);
            else
                WinAuthEndPoint.AuthenticatedResponse(args.HttpClient.Data);
        }

        response.SetOriginalHeaders();
        if (!response.Locked) await OnBeforeResponse(args);

        response = args.HttpClient.Response;

        var clientStream = args.ClientStream;
        if (response.Locked)
        {
            // write custom user response with body and return.
            await clientStream.WriteResponseAsync(response, cancellationToken);

            if (args.HttpClient.HasConnection && !args.HttpClient.CloseServerConnection)
                await args.SyphonOutBodyAsync(false, cancellationToken);

            return;
        }
        if (args.ReRequest)
        {
            if (args.HttpClient.HasConnection) await TcpConnectionFactory.Release(args.HttpClient.Connection);

            await args.ClearResponse(cancellationToken);
            var result = await HandleHttpSessionRequest(args, null, args.ClientConnection.NegotiatedApplicationProtocol,
                cancellationToken, args.CancellationTokenSource);
            if (result.LatestConnection != null) args.HttpClient.SetConnection(result.LatestConnection);

            return;
        }

        response.Locked = true;

        if (!args.IsTransparent && !args.IsSocks) response.Headers.FixProxyHeaders();

        await clientStream.WriteResponseAsync(response, cancellationToken);

        if (response.OriginalHasBody)
        {
            if (response.IsBodySent)
            {
                await args.SyphonOutBodyAsync(false, cancellationToken);
            }
            else
            {
                var serverStream = args.HttpClient.Connection.Stream;
                await serverStream.CopyBodyAsync(response, false, clientStream, TransformationMode.None,
                    false, args, cancellationToken);
            }

            response.IsBodyReceived = true;
        }

        args.TimeLine["Response Sent"] = DateTime.UtcNow;
    }

    private async Task OnBeforeResponse(SessionEventArgs args)
    {
        if (BeforeResponse != null) await BeforeResponse.InvokeAsync(this, args, ExceptionFunc);
    }

    private async Task OnAfterResponse(SessionEventArgs args)
    {
        if (AfterResponse != null) await AfterResponse.InvokeAsync(this, args, ExceptionFunc);
    }
#if DEBUG
        internal bool ShouldCallBeforeResponseBodyWrite()
        {
            if (OnResponseBodyWrite != null)
            {
                return true;
            }

            return false;
        }

        internal async Task OnBeforeResponseBodyWrite(BeforeBodyWriteEventArgs args)
        {
            if (OnResponseBodyWrite != null)
            {
                await OnResponseBodyWrite.InvokeAsync(this, args, ExceptionFunc);
            }
        }
#endif
}