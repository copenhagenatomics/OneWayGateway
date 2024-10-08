﻿using System.Net.Http.Headers;
using System.Text;

namespace UdpToHttpGateway.Client;

/// <remarks>
/// This writer has mostly been built for use with the also limited parser of the UdpToHttpGateway.
/// 
/// It should do fine with <see cref="HttpRequestMessage"/> messages in general, but you should carefully test it works for your type of messages.
/// 
/// One known limitation is that it deals poorly with some cookie headers, which we are not expecting to be used given the one way nature of the gateway.
/// </remarks>
static class LimitedHttpWriter
{
    const string TooLargeMessage = "Requests larger than 65536 bytes are not currently supported";
    static readonly Encoding HeadersEncoding = Encoding.UTF8;
    static readonly byte[] Space = HeadersEncoding.GetBytes(" ");
    static readonly byte[] CRLF = HeadersEncoding.GetBytes("\r\n");
    static readonly byte[] ColonSpace = HeadersEncoding.GetBytes(": ");
    static readonly byte[] HttpVersionPrefix = HeadersEncoding.GetBytes("HTTP/");
    static readonly byte[] HostPrefix = HeadersEncoding.GetBytes("Host: ");
    static readonly byte[] SingleByteBuffer = GC.AllocateArray<byte>(length: 1, pinned: true);

    /// <summary>
    /// Sends the specified request through to the gateway.
    /// </summary>
    /// <param name="request">The request to be sent.</param>
    /// <param name="destination">The buffer to write to.</param>
    /// <param name="token">A cancellation token to abort the operation.</param>
    /// <returns>The number of bytes written to the destination.</returns>
    /// <exception cref="NotSupportedException">When the request does not have an absolute Uri or the message is too large.</exception>
    public static ValueTask<int> WriteTo(HttpRequestMessage request, Memory<byte> destination, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        Uri uri = request.RequestUri ?? throw new NotSupportedException($"Requests without an Uri are not supported.");
        if (!uri.IsAbsoluteUri) throw new NotSupportedException($"Requests with a relative Uri are not supported.");
        Span<byte> buff = destination.Span;

        AddAndAdvance(request.Method.Method, Space, ref buff);
        AddAndAdvance(uri.OriginalString, Space, ref buff);
        AddAndAdvance(HttpVersionPrefix, request.Version?.ToString(2) ?? "1.1", CRLF, ref buff);
        AddHostHeaderIfNotExplicitlySpecified(request, uri, ref buff);//the host header is required for a valid request (although the gateway would add it automatically if missing)
        SerializeHeaderFields(request.Headers, ref buff);
        SerializeHeaderFields(request.Content?.Headers, ref buff);
        AddAndAdvance(string.Empty, CRLF, ref buff);//an empty line signals the end of the header

        return AddContent(request, destination, destination.Length - buff.Length, token);
    }

    static void AddHostHeaderIfNotExplicitlySpecified(HttpRequestMessage request, Uri uri, ref Span<byte> buff)
    {
        if (request.Headers.Host != null) return;
        AddAndAdvance(HostPrefix, uri.Authority, CRLF, ref buff);
    }

    static async ValueTask<int> AddContent(HttpRequestMessage request, Memory<byte> destination, int contentStartIndex, CancellationToken token)
    {
        int totalBytes = contentStartIndex;
        if (request.Content is HttpContent content)
        {
            Stream? stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            if (stream is null || !stream.CanRead) throw new NotSupportedException("Request content without a readable stream is not supported.");
            totalBytes += await CopyTo(stream, destination[contentStartIndex..], token).ConfigureAwait(false);
        }

        return totalBytes;
    }

    static async Task<int> CopyTo(Stream stream, Memory<byte> destination, CancellationToken token)
    {
        int totalBytes = 0;
        Memory<byte> rest = destination;
        while ((await stream.ReadAsync(rest, token).ConfigureAwait(false)) is int readBytes and not 0)
        {
            totalBytes += readBytes;
            rest = rest[readBytes..];
        }

        if (totalBytes < destination.Length)
            return totalBytes;

        //confirm there is no more data beyond what fits in the destination by trying to read an extra byte
        int lastReadBytes = await stream.ReadAsync(SingleByteBuffer, token).ConfigureAwait(false);
        return lastReadBytes == 0 ? totalBytes : throw new NotSupportedException(TooLargeMessage);
    }

    static void SerializeHeaderFields(HttpHeaders? headers, ref Span<byte> destination)
    {
        if (headers is null) return;

        foreach (KeyValuePair<string, IEnumerable<string>> header in headers)
        {//note given the one way nature of the gateway we don't support cookies, so we just ignore them here (cookies would need special treatment: https://www.rfc-editor.org/rfc/rfc7540#section-8.1.2.5)
            AddAndAdvance(header.Key, ColonSpace, ref destination);
            char separator = "User-Agent".Equals(header.Key, StringComparison.Ordinal) ? ' ' : ',';
            AddAndAdvance(string.Join(separator, header.Value), CRLF, ref destination);
        }
    }

    static void AddAndAdvance(string value, in ReadOnlySpan<byte> suffix, ref Span<byte> destination)
    {
        if (!Encoding.UTF8.TryGetBytes(value, destination, out int writtenBytes)) throw new NotSupportedException(TooLargeMessage);
        destination = destination[writtenBytes..];
        if (!suffix.TryCopyTo(destination)) throw new NotSupportedException(TooLargeMessage);
        destination = destination[suffix.Length..];
    }

    static void AddAndAdvance(in ReadOnlySpan<byte> prefix, string value, in ReadOnlySpan<byte> suffix, ref Span<byte> destination)
    {
        if (!prefix.TryCopyTo(destination)) throw new NotSupportedException(TooLargeMessage);
        destination = destination[prefix.Length..];
        if (!Encoding.UTF8.TryGetBytes(value, destination, out int writtenBytes)) throw new NotSupportedException(TooLargeMessage);
        destination = destination[writtenBytes..];
        if (!suffix.TryCopyTo(destination)) throw new NotSupportedException(TooLargeMessage);
        destination = destination[suffix.Length..];
    }
}
