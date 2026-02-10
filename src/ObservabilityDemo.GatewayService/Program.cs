using System.Diagnostics;
using Serilog;
using ObservabilityDemo.Shared.Constants;
using ObservabilityDemo.Shared.Kafka;
using ObservabilityDemo.Shared.Telemetry;
using ObservabilityDemo.GatewayService.Workers;

// 1. Configure Serilog
Log.Logger = TelemetrySetup.ConfigureSerilog(ServiceNames.ApiGateway);

try
{
    Log.Information("=== GatewayService Starting ===");

    var builder = Host.CreateApplicationBuilder(args);
    builder.Services.AddSerilog();

    // 2. Create ActivitySource for ApiGateway
    var gatewaySource = new ActivitySource(ServiceNames.ApiGateway);
    builder.Services.AddSingleton(gatewaySource);

    // 3. Create TracerProvider with gRPC client instrumentation
    var gatewayTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.ApiGateway, options: new TracerOptions
    {
        AddGrpcClientInstrumentation = true
    });
    builder.Services.AddSingleton(gatewayTracer);

    // 4. Create gRPC client channels to downstream services
    var playerGameChannel = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost:5200");
    var financeChannel = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost:5300");
    builder.Services.AddSingleton(new GrpcChannels(playerGameChannel, financeChannel));

    // 5. Create Kafka producer for workflow events
    var kafkaProducer = new KafkaProducer("localhost:9092", gatewaySource);
    builder.Services.AddSingleton(kafkaProducer);

    // 6. Register workflow workers
    builder.Services.AddHostedService<BettingWorkflowWorker>();
    builder.Services.AddHostedService<DepositWorkflowWorker>();
    builder.Services.AddHostedService<WithdrawalWorkflowWorker>();

    var host = builder.Build();

    host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
    {
        playerGameChannel.Dispose();
        financeChannel.Dispose();
        kafkaProducer.Dispose();
        gatewayTracer.Dispose();
    });

    Log.Information("GatewayService workflows: BettingWorkflow, DepositWorkflow, WithdrawalWorkflow");
    Log.Information("gRPC targets: PlayerGameService(5200), FinanceService(5300)");

    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "GatewayService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// gRPC channel container for DI
/// </summary>
public record GrpcChannels(
    Grpc.Net.Client.GrpcChannel PlayerGameChannel,
    Grpc.Net.Client.GrpcChannel FinanceChannel);
