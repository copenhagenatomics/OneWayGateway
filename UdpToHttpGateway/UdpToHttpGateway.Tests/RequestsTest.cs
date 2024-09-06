using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using UdpToHttpGateway.Client;

namespace UdpToHttpGateway.Tests;

[TestClass]
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member - This is a false positive, as ms test requires the tests to be public https://github.com/dotnet/roslyn-analyzers/issues/7192
#pragma warning disable CA1515 // Consider making public types internal - This is a false positive, as ms test requires the tests to be public https://github.com/dotnet/roslyn-analyzers/issues/7192
public class RequestsTest
#pragma warning restore CA1515 // Consider making public types internal
{
    const int RequestTimeout = 1000;
    const int HttpPort = 9080;
    static IPAddress? testIp;

    [ClassInitialize]
    public static async Task Initialize(TestContext _)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(string.Empty, AddressFamily.InterNetwork).ConfigureAwait(false);
        testIp = addresses.First(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
    }

    [TestMethod]
    public Task TestRootGetRequest() => TestRequest(HttpMethod.Get, "/", default(byte[]?), $"""
 GET http://{testIp}:9080/ HTTP/1.1
 Host: 172.19.253.73:9080


 """);

    [TestMethod]
    public Task TestRootPostRequest() => TestRequest(HttpMethod.Post, "/", [1, 2, 3, 4, 5], $"""
 POST http://{testIp}:9080/ HTTP/1.1
 Host: 172.19.253.73:9080
 Content-Length: 5


 """);

    [TestMethod]
    public Task Test65kRequest() => TestRequest(HttpMethod.Post, "/", Enumerable.Repeat((byte)9, 65000).ToArray(), $"""
 POST http://{testIp}:9080/ HTTP/1.1
 Host: 172.19.253.73:9080
 Content-Length: 65000


 """);

    [TestMethod]
    public Task TestJsonRequest()
    {
        using var content = JsonContent.Create(new { MyValues = "abc" });
        return TestRequest(HttpMethod.Post, "/", content, $"""
 POST http://{testIp}:9080/ HTTP/1.1
 Host: 172.19.253.73:9080
 Content-Type: application/json; charset=utf-8
 Content-Length: 18


 """);
    }

    static Task TestRequest(HttpMethod method, string relativeUri, byte[]? contentBytes, string expectedHeader)
    {
        using ReadOnlyMemoryContent? content = contentBytes != null ? new ReadOnlyMemoryContent(contentBytes) : null;
        return TestRequest(method, relativeUri, content, expectedHeader);
    }

    static async Task TestRequest(HttpMethod method, string relativeUri, HttpContent? content, string expectedHeader)
    {
        var received = Channel.CreateUnbounded<(string, byte[])>();
        using HttpRequestMessage request = new(method, new Uri(new Uri($"http://{testIp}:{HttpPort}"), relativeUri));
        if (content != null)
            request.Content = content;
        using var gatewayClient = new GatewayClient(IPEndPoint.Parse("127.0.0.1:4280"));
        WebApplication web = CreateFakeServer(HttpPort, received);
        await using System.Runtime.CompilerServices.ConfiguredAsyncDisposable _ = web.ConfigureAwait(false);
        await gatewayClient.Send(request).ConfigureAwait(false);
        await AssertFakeServerReceivedExpectedRequest(content != null ? await content.ReadAsByteArrayAsync().ConfigureAwait(false) : [], expectedHeader, received).ConfigureAwait(false);
        await web.StopAsync().ConfigureAwait(false);
    }


    static async Task AssertFakeServerReceivedExpectedRequest(byte[] expectedContent, string expectedHeader, Channel<(string, byte[])> received)
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            //Note the headers string received in the channel is a conversion from the typed headers received by the fake server to string done by test code in ToRaw, so its not exactly what was send by the gateway.
            //However, the header has first been parsed by asp.net, so we get some level of validation of how the headers look for receiving servers.
            (string headers, byte[] content) = await received.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
            Assert.AreEqual(expectedHeader, headers, $"Headers did not match. ExpectedToUtf8ToHex: {Convert.ToHexString(Encoding.UTF8.GetBytes(expectedHeader))}. ActualToUtf8ToHex: {Convert.ToHexString(Encoding.UTF8.GetBytes(headers))}");
            CollectionAssert.AreEqual(expectedContent, content, $"Content did not match. ExpectedToHex: {Convert.ToHexString(expectedContent)}. ActualToHex: {Convert.ToHexString(content)}");
        }
        catch (OperationCanceledException)
        {
            Assert.Fail("Timed out waiting to receive the http request from the gateway.");
        }
    }

    static WebApplication CreateFakeServer(int httpPort, Channel<(string, byte[])> received)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        _ = builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(3));//hard stop after 3 seconds.
        WebApplication app = builder.Build();
        app.Urls.Add($"http://+:{httpPort}");
        _ = app.Map("/", async (HttpRequest request) =>
        {
            await received.Writer.WriteAsync(await ToRaw(request).ConfigureAwait(false)).ConfigureAwait(false);
            return "OK";
        });
        _ = app.RunAsync();
        return app;
    }

    static async Task<(string, byte[])> ToRaw(HttpRequest request)
    {
        using var writer = new StringWriter();
        WriteStartLine(request, writer);
        WriteHeaders(request, writer);
        using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream).ConfigureAwait(false);
        stream.Position = 0;
        return (writer.ToString(), stream.ToArray());
    }

    static void WriteStartLine(HttpRequest request, StringWriter writer)
    {
        const string SPACE = " ";

        writer.Write(request.Method);
        writer.Write(SPACE + request.GetDisplayUrl());
        writer.Write(SPACE + request.Protocol);
        writer.Write("\r\n");
    }

    static void WriteHeaders(HttpRequest request, StringWriter writer)
    {
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> kvp in request.Headers)
        {
            writer.Write(string.Format("{0}: {1}", kvp.Key, kvp.Value));
            writer.Write("\r\n");
        }

        writer.Write("\r\n");
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
