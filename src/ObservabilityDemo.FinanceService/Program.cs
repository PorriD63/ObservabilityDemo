using System.Diagnostics;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using ObservabilityDemo.Shared.Constants;
using ObservabilityDemo.Shared.Kafka;
using ObservabilityDemo.Shared.Telemetry;
using ObservabilityDemo.FinanceService.Services;

// 配置 Serilog
Log.Logger = TelemetrySetup.ConfigureSerilog("FinanceService");

try
{
    Log.Information("=== FinanceService Starting (Port 5300) ===");

    var builder = WebApplication.CreateBuilder(args);

    // 替換預設 logging 為 Serilog
    builder.Services.AddSerilog();

    // 配置 Kestrel: HTTP/2 only (gRPC)
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenLocalhost(5300, o => o.Protocols = HttpProtocols.Http2);
    });

    // 註冊 gRPC
    builder.Services.AddGrpc();

    // 建立各邏輯服務的 ActivitySource（每個服務獨立的 trace 來源）
    var walletSource = new ActivitySource(ServiceNames.WalletService);
    var paymentSource = new ActivitySource(ServiceNames.PaymentService);
    var riskSource = new ActivitySource(ServiceNames.RiskService);

    // 建立各邏輯服務的 TracerProvider（每個服務有獨立的 service.name 資源標籤）
    // → Tempo service graph 會顯示 WalletService、PaymentService、RiskService 三個節點
    var walletTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.WalletService);
    var paymentTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.PaymentService);
    var riskTracer = TelemetrySetup.BuildTracerProvider(ServiceNames.RiskService);

    // 建立各邏輯服務的 KafkaProducer（PRODUCER span 會歸屬到正確的 service.name）
    var walletKafka = new KafkaProducer("localhost:9092", walletSource);
    var paymentKafka = new KafkaProducer("localhost:9092", paymentSource);
    var riskKafka = new KafkaProducer("localhost:9092", riskSource);

    // 註冊各 gRPC service 的依賴（使用 nested record 避免 DI 型別衝突）
    builder.Services.AddSingleton(new WalletGrpcService.Dependencies(walletSource, walletKafka));
    builder.Services.AddSingleton(new PaymentGrpcService.Dependencies(paymentSource, paymentKafka));
    builder.Services.AddSingleton(new RiskGrpcService.Dependencies(riskSource, riskKafka));

    var app = builder.Build();

    // 映射 gRPC services
    app.MapGrpcService<WalletGrpcService>();
    app.MapGrpcService<PaymentGrpcService>();
    app.MapGrpcService<RiskGrpcService>();

    // 應用程式停止時清理資源
    app.Lifetime.ApplicationStopping.Register(() =>
    {
        walletKafka.Dispose();
        paymentKafka.Dispose();
        riskKafka.Dispose();
        walletTracer.Dispose();
        paymentTracer.Dispose();
        riskTracer.Dispose();
    });

    Log.Information("FinanceService listening on port 5300 (gRPC)");
    Log.Information("Hosting services: {Services}",
        new[] { ServiceNames.WalletService, ServiceNames.PaymentService, ServiceNames.RiskService });

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "FinanceService terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
