using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using System.Net;
using System.Net.Http.Json;
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
    const int RequestTimeout = 300;
    const int HttpPort = 9080;
    const string TestAddress = "127.0.0.1";
    static readonly IPEndPoint GatewayIp = IPEndPoint.Parse("127.0.0.1:4280");

    [TestMethod]
    public Task TestRootGetRequest() => TestRequest(HttpMethod.Get, "/", default(byte[]?), $"""
 GET http://{TestAddress}:{HttpPort}/ HTTP/1.1
 Host: {TestAddress}:{HttpPort}
 

 """);

    [TestMethod]
    public Task TestRootPostRequest() => TestRequest(HttpMethod.Post, "/", [1, 2, 3, 4, 5], $"""
 POST http://{TestAddress}:{HttpPort}/ HTTP/1.1
 Host: {TestAddress}:{HttpPort}
 Content-Length: 5


 """);

    [TestMethod]
    public Task Test65kRequest() => TestRequest(HttpMethod.Post, "/", Enumerable.Repeat((byte)9, 65000).ToArray(), $"""
 POST http://{TestAddress}:{HttpPort}/ HTTP/1.1
 Host: {TestAddress}:{HttpPort}
 Content-Length: 65000


 """);

    [TestMethod]
    public Task TestJsonRequest() => TestRequest(HttpMethod.Post, "/", () => JsonContent.Create(new { MyValues = "abc" }), $"""
 POST http://{TestAddress}:{HttpPort}/ HTTP/1.1
 Host: {TestAddress}:{HttpPort}
 Content-Type: application/json; charset=utf-8
 Content-Length: 18


 """);

    [TestMethod]
    public async Task TestSendingInParallel()
    {
        var received = Channel.CreateUnbounded<(string headers, byte[] content)>();
        WebApplication web = CreateFakeServer(HttpPort, received);
        await using System.Runtime.CompilerServices.ConfiguredAsyncDisposable _ = web.ConfigureAwait(false);
        using var handler = new SendViaUdpGatewayMessageHandler(GatewayIp);
        using var client = new HttpClient(handler);

        (string expectedHeader, List<byte[]> contentToSend) = GetRequestDataToSend(100);
        await Parallel.ForEachAsync(contentToSend, async (c, _) =>
        {
            await SendHttpClientRequest(client, HttpMethod.Post, "/", () => new ReadOnlyMemoryContent(c)).ConfigureAwait(false);
        }).ConfigureAwait(false);

        List<(string headers, byte[] content)> all = await GetReceivedOrderedByFirstByteOfContent(received, contentToSend.Count).ConfigureAwait(false);
        for (int i = 0; i < all.Count; i++)
            AssertExpectedValues(expectedHeader, contentToSend[i], all[i].headers, all[i].content);

        await web.StopAsync().ConfigureAwait(false);
    }

    static (string expectedHeader, List<byte[]>) GetRequestDataToSend(int count)
    {
        string expectedHeader = $"""
 POST http://{TestAddress}:{HttpPort}/ HTTP/1.1
 Host: {TestAddress}:{HttpPort}
 Content-Length: 10


 """;
        var contentToSend = Enumerable.Range(0, count).Select(i => Enumerable.Range(i, 10).Select(i => (byte)i).ToArray()).ToList();
        return (expectedHeader, contentToSend);
    }

    static void AssertExpectedValues(string expectedHeader, byte[] expectedContent, string headers, byte[] content)
    {
        Assert.AreEqual(expectedHeader, headers, $"Headers did not match. ExpectedToUtf8ToHex: {Convert.ToHexString(Encoding.UTF8.GetBytes(expectedHeader))}. ActualToUtf8ToHex: {Convert.ToHexString(Encoding.UTF8.GetBytes(headers))}");
        CollectionAssert.AreEqual(expectedContent, content, $"Content did not match. ExpectedToHex: {Convert.ToHexString(expectedContent)}. ActualToHex: {Convert.ToHexString(content)}");
    }

    static async Task<List<(string headers, byte[] content)>> GetReceivedOrderedByFirstByteOfContent(
        Channel<(string headers, byte[] content)> received, int count)
    {
        using var cts = new CancellationTokenSource(3000);
        var all = new List<(string headers, byte[] content)>(100);
        await foreach ((string headers, byte[] content) r in received.Reader.ReadAllAsync(cts.Token).ConfigureAwait(false))
        {
            all.Add(r);
            if (all.Count == 100) break;
        }

        all = [.. all.OrderBy(a => a.content[0])];
        Assert.AreEqual(count, all.Count);
        return all;
    }

    static Task TestRequest(HttpMethod method, string relativeUri, byte[]? contentBytes, string expectedHeader) =>
        TestRequest(method, relativeUri, () => contentBytes != null ? new ReadOnlyMemoryContent(contentBytes) : null, expectedHeader);

    static async Task TestRequest(HttpMethod method, string relativeUri, Func<HttpContent?> getContent, string expectedHeader)
    {
        var received = Channel.CreateUnbounded<(string, byte[])>();
        WebApplication web = CreateFakeServer(HttpPort, received);
        await using System.Runtime.CompilerServices.ConfiguredAsyncDisposable _ = web.ConfigureAwait(false);
        await TestGatewayClientRequest(method, relativeUri, getContent, expectedHeader, received).ConfigureAwait(false);
        await TestHttpClientRequest(method, relativeUri, getContent, expectedHeader, received).ConfigureAwait(false);
        await web.StopAsync().ConfigureAwait(false);
    }

    static async ValueTask TestGatewayClientRequest(HttpMethod method, string relativeUri, Func<HttpContent?> getContent, string expectedHeader, Channel<(string, byte[])> received)
    {
        using HttpRequestMessage request = NewRequest(method, relativeUri, getContent);
        using var gatewayClient = new GatewayClient(GatewayIp);
        await gatewayClient.Send(request).ConfigureAwait(false);
        await AssertReceivedExpectedRequestValues(expectedHeader, received, request.Content).ConfigureAwait(false);
    }

    static async ValueTask TestHttpClientRequest(HttpMethod method, string relativeUri, Func<HttpContent?> getContent, string expectedHeader, Channel<(string, byte[])> received)
    {
        using var handler = new SendViaUdpGatewayMessageHandler(GatewayIp);
        using var client = new HttpClient(handler);
        await SendHttpClientRequest(client, method, relativeUri, getContent).ConfigureAwait(false);
        using HttpContent? expectedContent = getContent();
        await AssertReceivedExpectedRequestValues(expectedHeader, received, expectedContent).ConfigureAwait(false);
    }

    static async ValueTask SendHttpClientRequest(HttpClient client, HttpMethod method, string relativeUri, Func<HttpContent?> getContent)
    {
        using HttpRequestMessage request = NewRequest(method, relativeUri, getContent);
        HttpResponseMessage res = await client.SendAsync(request).ConfigureAwait(false);
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
    }

    static async Task AssertReceivedExpectedRequestValues(string expectedHeader, Channel<(string, byte[])> received, HttpContent? expectedHttpContent)
    {
        byte[] expectedContent = expectedHttpContent != null ? await expectedHttpContent.ReadAsByteArrayAsync().ConfigureAwait(false) : [];
        await AssertFakeServerReceivedExpectedRequest(expectedContent, expectedHeader, received).ConfigureAwait(false);
    }

    static HttpRequestMessage NewRequest(HttpMethod method, string relativeUri, Func<HttpContent?> getContent) =>
        new(method, new Uri(new Uri($"http://{TestAddress}:{HttpPort}"), relativeUri)) { Content = getContent() };
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
        writer.Write("\n");
    }

    static void WriteHeaders(HttpRequest request, StringWriter writer)
    {
        foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> kvp in request.Headers)
        {
            writer.Write(string.Format("{0}: {1}", kvp.Key, kvp.Value));
            writer.Write("\n");
        }

        writer.Write("\n");
    }
}
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
