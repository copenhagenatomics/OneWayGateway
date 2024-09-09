using System.Net.Sockets;
using System.Net;
using Microsoft.Extensions.Options;

namespace UdpToHttpGateway;

#pragma warning disable CA1812 //https://github.com/dotnet/roslyn-analyzers/issues/6561
sealed partial class UdpReceiver(IOptions<GatewayOptions> options, ILogger<UdpReceiver> logger) : BackgroundService
#pragma warning restore CA1812
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using Socket socket = await Bind(IPEndPoint.Parse(options.Value.EndPoint), stoppingToken).ConfigureAwait(false);
        const int MaxUDPSize = 0x10000; //same System.Net.Sockets.UdpClient uses, which makes the receiving buffer larger than the max for ipv4 udp packets
        byte[] buffer = GC.AllocateArray<byte>(length: MaxUDPSize, pinned: true);
        Memory<byte> bufferMem = buffer;
        SocketAddress receivedAddress = new(socket.AddressFamily);
        long totalReceivedBytes = 0, totalReceivedPackets = 0;
        using var client = new HttpClient();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                //For now we just send the request directly, but in later versions there should be a separate sender.
                //This also means we should have a set of buffers to receive in, which should get freed as they get sent or discarded (if we keep receiving at a rate higher than can be sent to the remote)
                int receivedBytes = await socket.ReceiveFromAsync(bufferMem, SocketFlags.None, receivedAddress, stoppingToken).ConfigureAwait(false);
                totalReceivedBytes += receivedBytes;
                totalReceivedPackets++;
                await ParseAndSendMessage(client, bufferMem[..receivedBytes], stoppingToken).ConfigureAwait(false);
            }
        }
        finally
        {
            LogReceivedData(DateTimeOffset.Now, totalReceivedPackets, totalReceivedBytes);
        }
    }

    async ValueTask ParseAndSendMessage(HttpClient client, Memory<byte> bytes, CancellationToken stoppingToken)
    {
        HttpRequestMessage? message = null;
        try
        {
            bool succeeded = LimitedHttpParser.TryParseMessage(bytes, out message);
            try
            {
                if (succeeded && message != null)
                {
                    HttpResponseMessage response = await client.SendAsync(message, stoppingToken).ConfigureAwait(false);
                    LogSending(DateTimeOffset.UtcNow, response, message);
                }
                else
                    LogParsingError(DateTimeOffset.UtcNow, bytes);
            }
            finally
            {
                message?.Dispose();
            }
        }
        catch (HttpRequestException ex)
        {
            LogSendingError(DateTimeOffset.UtcNow, message, ex);
        }
    }

    async Task<Socket> Bind(IPEndPoint endpoint, CancellationToken stoppingToken)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(string.Empty, AddressFamily.InterNetwork, stoppingToken).ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new NotSupportedException("Udp Receiver did not detect any ip addresses");
        }

        LogListeningIP(DateTimeOffset.UtcNow, endpoint, addresses);

        Socket socket = new(endpoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(endpoint);
        return socket;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{time} - received response {response} for message {message}.")]
    partial void LogSending(DateTimeOffset time, HttpResponseMessage response, HttpRequestMessage message);
    [LoggerMessage(Level = LogLevel.Warning, Message = "{time} - unexpected error sending {message}.")]
    partial void LogSendingError(DateTimeOffset time, HttpRequestMessage? message, Exception ex);
    [LoggerMessage(Level = LogLevel.Warning, Message = "{time} - unexpected parsing error sending {message}.")]
    partial void LogParsingError(DateTimeOffset time, Memory<byte> message);

    [LoggerMessage(Level = LogLevel.Information, Message = "{time} - listening on {endpoint} - detected addresses {addresses}.")]
    partial void LogListeningIP(DateTimeOffset time, EndPoint endPoint, IPAddress[] addresses);

    [LoggerMessage(Level = LogLevel.Information, Message = "{time} - {packets} packets received with a total of {bytes} bytes.")]
    partial void LogReceivedData(DateTimeOffset time, long packets, long bytes);
}
