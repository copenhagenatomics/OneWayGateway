#pragma warning disable CA1812 //https://github.com/dotnet/roslyn-analyzers/issues/6561
sealed class GatewayOptions
#pragma warning restore CA1812
{
    public required string EndPoint { get; set; } = "0.0.0.0:4280";
}