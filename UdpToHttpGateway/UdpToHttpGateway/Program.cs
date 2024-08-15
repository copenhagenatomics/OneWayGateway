using UdpToHttpGateway;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<UdpReceiverOptions>(builder.Configuration.GetRequiredSection(nameof(UdpReceiverOptions)));
builder.Services.AddHostedService<UdpReceiver>();

builder.Build().Run();
