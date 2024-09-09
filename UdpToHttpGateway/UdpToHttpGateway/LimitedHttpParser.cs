using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace UdpToHttpGateway;

/// <remarks>
/// This parser has mostly been built for use with the also limited writer of the gateway client.
/// 
/// It should do fine with http 1.1 messages in general, but you should carefully test when using an alternate client.
/// 
/// One known limitation is that it deals poorly with some cookie headers, which we are not expecting to be used given the one way nature of the gateway.
/// </remarks>
static class LimitedHttpParser
{
    static readonly Encoding UTF8 = Encoding.UTF8;//important: the parsing assumes UTF8 for lookups of the below sequences, so care must be taken if changing this.
    static readonly byte[] DoubleCRLF = UTF8.GetBytes("\r\n\r\n");
    static readonly byte[] CRLF = UTF8.GetBytes("\r\n");
    static readonly byte[] Space = UTF8.GetBytes(" ");
    static readonly byte[] Slash = UTF8.GetBytes("/");
    static readonly byte[] ColonSpace = UTF8.GetBytes(": ");

    public static bool TryParseMessage(Memory<byte> bytesMemory, [NotNullWhen(true)] out HttpRequestMessage? request)
    {
        Span<byte> bytes = bytesMemory.Span;
        request = null;
        if (!TryGetLine(ref bytes, out Span<byte> requestLine) ||
            !TryGetNextToken(ref requestLine, out string? method, Space) ||
            !TryGetNextToken(ref requestLine, out string? uri, Space) ||
            !TryGetNextToken(ref requestLine, out string? _, Slash) ||
            !TryGetNextToken(ref requestLine, out string? version, CRLF) ||
            !Version.TryParse(version, out Version? typedVersion))
            return false;
        request = new HttpRequestMessage(HttpMethod.Parse(method), uri) { Version = typedVersion };

        int endOfHeaderIndex = bytes.IndexOf(DoubleCRLF);
        if (endOfHeaderIndex < 0)
            return false;

        int headersLength = endOfHeaderIndex + DoubleCRLF.Length;
        int contentLength = bytes.Length - headersLength;
        if (contentLength > 0)
            request.Content = new ReadOnlyMemoryContent(bytesMemory[^contentLength..]);

        bytes = bytes[..headersLength];
        while (TryGetLine(ref bytes, out Span<byte> headerLine) && !headerLine.IsEmpty)
        {
            if (!TryGetNextToken(ref headerLine, out string? name, ColonSpace) ||
                !TryGetNextToken(ref headerLine, out string? value, CRLF) ||
                (
                    !request.Headers.TryAddWithoutValidation(name, value) &&
                    request.Content?.Headers.TryAddWithoutValidation(name, value) == false)
                )
                return false;
        }

        return true;
    }

    static bool TryGetLine(ref Span<byte> bytes, out Span<byte> requestLineBytes)
    {
        requestLineBytes = [];
        int endOfRequestLine = bytes.IndexOf(CRLF);
        if (endOfRequestLine <= 0)
            return false;

        requestLineBytes = bytes[0..(endOfRequestLine + CRLF.Length)];
        bytes = bytes[requestLineBytes.Length..];
        return true;
    }

    static bool TryGetNextToken(ref Span<byte> bytes, [NotNullWhen(true)] out string? token, ReadOnlySpan<byte> separator)
    {
        token = null;
        int endOfToken = bytes.IndexOf(separator);
        if (endOfToken <= 0)
            return false;

        token = UTF8.GetString(bytes[0..endOfToken]);
        bytes = bytes[(endOfToken + separator.Length)..];
        return true;
    }
}
