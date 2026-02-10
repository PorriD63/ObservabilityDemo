using System.Diagnostics;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Events;
using SeqDemo.Shared.Kafka;

namespace SeqDemo.NotificationService.Workers;

/// <summary>
/// BackgroundService — 消費所有 Kafka topics，處理通知邏輯。
/// 從 Kafka headers 提取 traceparent，用 parentContext 接續同一 TraceId。
/// </summary>
public class NotificationWorker : BackgroundService
{
    private readonly ActivitySource _activitySource;
    private readonly ILogger<NotificationWorker> _logger;
    private const string BootstrapServers = "localhost:9092";
    private const string GroupId = "notification-service";

    public NotificationWorker(ActivitySource activitySource, ILogger<NotificationWorker> logger)
    {
        _activitySource = activitySource;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 等待 Kafka 就緒
        await Task.Delay(3000, stoppingToken);

        Log.Information("NotificationWorker started, subscribing to all topics");

        using var consumer = new KafkaConsumer(BootstrapServers, GroupId, _activitySource);

        var topics = new[]
        {
            KafkaTopics.BetSettled,
            KafkaTopics.PaymentProcessed,
            KafkaTopics.WithdrawalApproved,
            KafkaTopics.WithdrawalFlagged,
            KafkaTopics.WorkflowCompleted,
            KafkaTopics.InsufficientBalance
        };

        consumer.Subscribe(topics);

        Log.Information("NotificationWorker subscribed to topics: {Topics}", topics);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var resultWithActivity = consumer.ConsumeWithTracing(TimeSpan.FromMilliseconds(500));
                if (resultWithActivity == null) continue;

                var result = resultWithActivity.Result;
                var topic = result.Topic;

                using (LogContext.PushProperty("ServiceName", ServiceNames.NotificationService))
                {
                    switch (topic)
                    {
                        case KafkaTopics.BetSettled:
                            HandleBetSettled(result);
                            break;

                        case KafkaTopics.PaymentProcessed:
                            HandlePaymentProcessed(result);
                            break;

                        case KafkaTopics.WithdrawalApproved:
                            HandleWithdrawalApproved(result);
                            break;

                        case KafkaTopics.WithdrawalFlagged:
                            HandleWithdrawalFlagged(result);
                            break;

                        case KafkaTopics.WorkflowCompleted:
                            HandleWorkflowCompleted(result);
                            break;

                        case KafkaTopics.InsufficientBalance:
                            HandleInsufficientBalance(result);
                            break;

                        default:
                            Log.Warning("收到未知 topic 的訊息: {Topic}", topic);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "NotificationWorker 處理訊息時發生錯誤");
                await Task.Delay(1000, stoppingToken);
            }
        }

        Log.Information("NotificationWorker stopped");
    }

    private static void HandleBetSettled(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<BetSettledEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "BetNotifier"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("BetId", evt.BetId))
        {
            Log.Information(
                "注單結算通知: {UserId} 的注單 {BetId} 已結算，下注 {BetAmount}，獲利 {Profit}，狀態: {Status}",
                evt.UserId, evt.BetId, evt.BetAmount, evt.Profit, evt.Status);
        }
    }

    private static void HandlePaymentProcessed(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<PaymentProcessedEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "PaymentNotifier"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("TransactionId", evt.TransactionId))
        {
            if (evt.Status == "Success")
            {
                Log.Information(
                    "支付成功通知: {UserId} 的交易 {TransactionId} 已完成，金額 {Amount}，手續費 {Fee}",
                    evt.UserId, evt.TransactionId, evt.Amount, evt.Fee);
            }
            else
            {
                Log.Warning(
                    "支付失敗通知: {UserId} 的交易 {TransactionId} 失敗，錯誤碼: {ErrorCode}",
                    evt.UserId, evt.TransactionId, evt.ErrorCode);
            }
        }
    }

    private static void HandleWithdrawalApproved(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<WithdrawalApprovedEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "WithdrawalNotifier"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("TransactionId", evt.TransactionId))
        {
            Log.Information(
                "提款核准通知: {UserId} 的提款 {TransactionId} 已核准，金額 {Amount} {Currency}",
                evt.UserId, evt.TransactionId, evt.Amount, evt.Currency);
        }
    }

    private static void HandleWithdrawalFlagged(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<WithdrawalFlaggedEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("TransactionId", evt.TransactionId))
        {
            Log.Warning(
                "提款風險標記通知: {UserId} 的提款 {TransactionId} 已標記待審核，原因: {Reason}，審核員: {Reviewer}",
                evt.UserId, evt.TransactionId, evt.Reason, evt.ReviewerAssigned);
        }
    }

    private static void HandleWorkflowCompleted(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<WorkflowCompletedEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "WorkflowNotifier"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("WorkflowName", evt.WorkflowName))
        {
            Log.Information(
                "{WorkflowName} completed for {UserId}",
                evt.WorkflowName, evt.UserId);
        }
    }

    private static void HandleInsufficientBalance(Confluent.Kafka.ConsumeResult<string, string> result)
    {
        var evt = KafkaConsumer.Deserialize<InsufficientBalanceEvent>(result);
        if (evt == null) return;

        using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
        using (LogContext.PushProperty("UserId", evt.UserId))
        using (LogContext.PushProperty("BetId", evt.BetId))
        {
            Log.Warning(
                "餘額不足通知: {UserId} 嘗試下注 {RequestedAmount} {Currency}，但餘額僅 {AvailableBalance}",
                evt.UserId, evt.RequestedAmount, evt.Currency, evt.AvailableBalance);
        }
    }
}
