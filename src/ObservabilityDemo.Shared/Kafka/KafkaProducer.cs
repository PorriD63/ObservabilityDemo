using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace ObservabilityDemo.Shared.Kafka;

/// <summary>
/// Kafka Producer — 發送訊息並注入 W3C traceparent 到 Kafka headers，
/// 讓 Consumer 端能接續同一個 TraceId。
/// </summary>
public class KafkaProducer : IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ActivitySource _activitySource;

    public KafkaProducer(string bootstrapServers, ActivitySource activitySource)
    {
        _activitySource = activitySource;
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// 發送事件到指定 topic，自動注入 traceparent
    /// </summary>
    public async Task ProduceAsync<T>(string topic, string key, T @event, CancellationToken cancellationToken = default)
    {
        using var activity = _activitySource.StartActivity($"kafka.produce {topic}", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.operation", "publish");

        var headers = new Headers();

        // 注入 W3C traceparent 到 Kafka headers
        if (activity != null)
        {
            var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
            headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
        }

        var message = new Message<string, string>
        {
            Key = key,
            Value = JsonSerializer.Serialize(@event),
            Headers = headers
        };

        await _producer.ProduceAsync(topic, message, cancellationToken);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}
