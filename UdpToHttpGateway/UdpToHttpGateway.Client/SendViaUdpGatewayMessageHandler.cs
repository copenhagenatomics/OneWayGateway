using Microsoft.Extensions.ObjectPool;
using System.Net;

namespace UdpToHttpGateway.Client;

/// <summary>
/// This <see cref="HttpMessageHandler"/> sends the http requests via the specified gateway.
/// </summary>
/// <param name="udpToHttpGatewayIP">the ip of the gateway.</param>
/// <remarks>
/// At the moment this handler does not support sending in parallel.
/// 
/// Note that since the gateway is strictly send only, the handler returns fake OK=200 responses.
/// </remarks>
public sealed class SendViaUdpGatewayMessageHandler(IPEndPoint udpToHttpGatewayIP) : HttpMessageHandler
{
    readonly DefaultObjectPool<GatewayClient> gatewayClientPool = new(new ClientObjectPolicy(udpToHttpGatewayIP));
    readonly GatewayClient client = new(udpToHttpGatewayIP);

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        GatewayClient client = gatewayClientPool.Get();
        try
        {
            await client.Send(request, cancellationToken).ConfigureAwait(false);
            return new(HttpStatusCode.OK);
        }
        finally
        {
            gatewayClientPool.Return(client);
        }
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        client.Dispose();
    }

    sealed class ClientObjectPolicy(IPEndPoint udpToHttpGatewayIP) : PooledObjectPolicy<GatewayClient>
    {
        public override GatewayClient Create() => new(udpToHttpGatewayIP);
        public override bool Return(GatewayClient obj) => true;
    }
}
