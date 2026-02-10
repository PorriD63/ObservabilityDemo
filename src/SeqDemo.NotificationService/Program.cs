using System.Diagnostics;
using Serilog;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Telemetry;
using SeqDemo.NotificationService.Workers;

// 配置 Serilog
Log.Logger = TelemetrySetup.ConfigureSerilog(ServiceNames.NotificationService);

try
{
    Log.Information("=== NotificationService Starting ===");

    var builder = Host.CreateApplicationBuilder(args);

    // 替換預設 logging 為 Serilog
    builder.Services.AddSerilog();

    // 註冊 ActivitySource (供 KafkaConsumer 使用)
    var activitySource = new ActivitySource(ServiceNames.NotificationService);
    builder.Services.AddSingleton(activitySource);

    // 註冊 TracerProvider
    var tracerProvider = TelemetrySetup.BuildTracerProvider(ServiceNames.NotificationService);
    builder.Services.AddSingleton(tracerProvider);

    // 註冊 NotificationWorker
    builder.Services.AddHostedService<NotificationWorker>();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "NotificationService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
