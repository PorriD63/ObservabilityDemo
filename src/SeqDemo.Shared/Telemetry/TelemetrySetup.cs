using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace SeqDemo.Shared.Telemetry;

public class TracerOptions
{
    public bool AddAspNetCoreInstrumentation { get; set; }
    public bool AddGrpcClientInstrumentation { get; set; }
}

public static class TelemetrySetup
{
    private const string DefaultOtlpEndpoint = "http://localhost:4317";

    /// <summary>
    /// 建立 TracerProvider（每個邏輯服務各一個，擁有獨立的 service.name）
    /// </summary>
    public static TracerProvider BuildTracerProvider(
        string serviceName,
        string otlpEndpoint = DefaultOtlpEndpoint,
        TracerOptions? options = null)
    {
        options ??= new TracerOptions();

        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName: serviceName, serviceNamespace: "Demo", serviceVersion: "1.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = "Demo"
                }))
            .AddSource(serviceName);

        if (options.AddAspNetCoreInstrumentation)
            builder.AddAspNetCoreInstrumentation();

        if (options.AddGrpcClientInstrumentation)
            builder.AddGrpcClientInstrumentation();

        builder.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));

        return builder.Build()!;
    }

    /// <summary>
    /// 配置 Serilog — 透過 OpenTelemetry Collector 統一發送到 Seq, Loki, Elasticsearch
    /// </summary>
    public static ILogger ConfigureSerilog(
        string serviceName,
        string otlpEndpoint = DefaultOtlpEndpoint)
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", serviceName)
            .Enrich.WithProperty("Environment", "Demo")
            .Enrich.With(new ActivityEnricher())
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}/{SourceContext}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = otlpEndpoint;
                options.Protocol = OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = serviceName,
                    ["service.namespace"] = "Demo",
                    ["deployment.environment"] = "Demo"
                };
            })
            .CreateLogger();
    }
}
