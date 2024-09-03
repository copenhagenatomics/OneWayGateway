using System.Net;
using System.Net.Sockets;

namespace UdpToHttpGateway.Client;

/// <remarks>
/// This client does not support parallel sends, use separate instances or an object pool.
/// </remarks>
public sealed class GatewayClient : IDisposable
{
    const int MaxUDPSize = 0x10000; //same UdpClient.MaxUDPSize uses
    readonly Socket GatewaySocket;
    readonly SocketAddress GatewayAddress;
    readonly byte[] Buffer = GC.AllocateArray<byte>(length: MaxUDPSize, pinned: true);

    /// <summary>
    /// Creates a <see cref="GatewayClient"/> that sends data to the specified gateway IP.
    /// </summary>
    /// <param name="udpToHttpGatewayIP">the ip of the gateway.</param>
    public GatewayClient(IPEndPoint udpToHttpGatewayIP)
    {
        ArgumentNullException.ThrowIfNull(udpToHttpGatewayIP);
        GatewaySocket = new(udpToHttpGatewayIP.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        GatewayAddress = udpToHttpGatewayIP.Serialize();
    }

    /// <summary>
    /// Sends the specified request through to the gateway.
    /// </summary>
    /// <param name="request">The request to be sent.</param>
    /// <param name="token">A cancellation token to abort the operation</param>
    /// <exception cref="NotSupportedException">When the request does not have an absolute Uri or the message is too large.</exception>
    public async ValueTask Send(HttpRequestMessage request, CancellationToken token = default)
    {
        Memory<byte> bufferMem = Buffer.AsMemory();
        int bytesWritten = await LimitedHttpWriter.WriteTo(request, bufferMem, token).ConfigureAwait(false);
        _ = await GatewaySocket.SendToAsync(bufferMem[0..bytesWritten], SocketFlags.None, GatewayAddress, token).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose() => GatewaySocket.Dispose();
}
