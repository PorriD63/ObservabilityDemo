using System.Diagnostics;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using ObservabilityDemo.Shared.Constants;
using ObservabilityDemo.Shared.Telemetry;
using ObservabilityDemo.PlayerGameService.Services;

// 配置 Serilog
Log.Logger = TelemetrySetup.ConfigureSerilog("PlayerGameService");

try
{
    Log.Information("=== PlayerGameService Starting (Port 5200) ===");

    var builder = WebApplication.CreateBuilder(args);

    // 替換預設 logging 為 Serilog
    builder.Services.AddSerilog();

    // 配置 Kestrel: HTTP/2 only (gRPC)
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5200, o => o.Protocols = HttpProtocols.Http2);
    });

    // 註冊 gRPC
    builder.Services.AddGrpc();

    // 建立各邏輯服務的 ActivitySource（每個服務獨立的 trace 來源）
    var playerSource = new ActivitySource(ServiceNames.PlayerService);
    var gameSource = new ActivitySource(ServiceNames.GameService);

    // 建立各邏輯服務的 TracerProvider（每個服務有獨立的 service.name 資源標籤）
    // → Tempo service graph 會顯示 PlayerService、GameService 兩個節點
    var playerTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.PlayerService);
    var gameTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.GameService, options: new TracerOptions
    {
        AddGrpcClientInstrumentation = true // GameService 需要呼叫 FinanceService
    });

    // 建立 gRPC client 連線到 FinanceService (Port 5300)
    var financeChannel = Grpc.Net.Client.GrpcChannel.ForAddress("http://localhost:5300");

    // 註冊各 gRPC service 的依賴
    builder.Services.AddSingleton(new PlayerGrpcService.Dependencies(playerSource));
    builder.Services.AddSingleton(new GameGrpcService.Dependencies(gameSource, financeChannel));

    var app = builder.Build();

    // 映射 gRPC services
    app.MapGrpcService<PlayerGrpcService>();
    app.MapGrpcService<GameGrpcService>();

    // 應用程式停止時清理資源
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        financeChannel.Dispose();
        playerTracer.Dispose();
        gameTracer.Dispose();
    });

    Log.Information("PlayerGameService listening on port 5200 (gRPC)");
    Log.Information("Hosting services: {Services}",
        new[] { ServiceNames.PlayerService, ServiceNames.GameService });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "PlayerGameService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
