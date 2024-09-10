using UdpToHttpGateway;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetRequiredSection(nameof(GatewayOptions)));
builder.Services.AddSystemd();
builder.Services.AddHostedService<UdpReceiver>();

builder.Build().Run();
