using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;

namespace SeqDemo.Shared.Kafka;

/// <summary>
/// Kafka Consumer — 消費訊息並從 Kafka headers 提取 traceparent，
/// 用 parentContext 接續同一個 TraceId。
/// </summary>
public class KafkaConsumer : IDisposable
{
    private readonly IConsumer<string, string> _consumer;
    private readonly ActivitySource _activitySource;

    public KafkaConsumer(string bootstrapServers, string groupId, ActivitySource activitySource)
    {
        _activitySource = activitySource;
        var config = new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = true
        };
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    public void Subscribe(IEnumerable<string> topics)
    {
        _consumer.Subscribe(topics);
    }

    /// <summary>
    /// 消費一筆訊息，自動提取 traceparent 並建立 CONSUMER span（接續同一 TraceId）
    /// </summary>
    public ConsumeResultWithActivity? ConsumeWithTracing(TimeSpan timeout)
    {
        var result = _consumer.Consume(timeout);
        if (result == null) return null;

        // 從 Kafka headers 提取 traceparent
        ActivityContext? parentContext = ExtractTraceParent(result.Message.Headers);

        // 建立 CONSUMER span，用 parentContext 接續同一 Trace
        var activity = parentContext.HasValue
            ? _activitySource.StartActivity(
                $"kafka.consume {result.Topic}",
                ActivityKind.Consumer,
                parentContext: parentContext.Value)
            : _activitySource.StartActivity(
                $"kafka.consume {result.Topic}",
                ActivityKind.Consumer);

        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.source.name", result.Topic);
        activity?.SetTag("messaging.operation", "receive");
        activity?.SetTag("messaging.kafka.consumer.group", _consumer.MemberId);

        return new ConsumeResultWithActivity(result, activity);
    }

    /// <summary>
    /// 從 Kafka message headers 解析 W3C traceparent
    /// 格式: "00-{traceId}-{spanId}-{flags}"
    /// </summary>
    private static ActivityContext? ExtractTraceParent(Headers? headers)
    {
        if (headers == null) return null;

        var traceparentHeader = headers.FirstOrDefault(h => h.Key == "traceparent");
        if (traceparentHeader == null) return null;

        var traceparent = Encoding.UTF8.GetString(traceparentHeader.GetValueBytes());
        var parts = traceparent.Split('-');
        if (parts.Length < 4) return null;

        var traceId = ActivityTraceId.CreateFromString(parts[1]);
        var spanId = ActivitySpanId.CreateFromString(parts[2]);
        var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

        return new ActivityContext(traceId, spanId, flags, isRemote: true);
    }

    /// <summary>
    /// 反序列化 Kafka 訊息為指定型別
    /// </summary>
    public static T? Deserialize<T>(ConsumeResult<string, string> result)
    {
        return JsonSerializer.Deserialize<T>(result.Message.Value);
    }

    public void Dispose()
    {
        _consumer.Close();
        _consumer.Dispose();
    }
}

/// <summary>
/// 包含 ConsumeResult 和對應的 Activity（用於 dispose）
/// </summary>
public sealed class ConsumeResultWithActivity : IDisposable
{
    public ConsumeResult<string, string> Result { get; }
    public Activity? Activity { get; }

    public ConsumeResultWithActivity(ConsumeResult<string, string> result, Activity? activity)
    {
        Result = result;
        Activity = activity;
    }

    public void Dispose()
    {
        Activity?.Dispose();
    }
}
