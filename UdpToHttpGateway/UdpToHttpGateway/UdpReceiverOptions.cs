#pragma warning disable CA1812 //https://github.com/dotnet/roslyn-analyzers/issues/6561
internal sealed class UdpReceiverOptions
#pragma warning restore CA1812
{
    public required string EndPoint { get; set; } = "0.0.0.0:4280";
}