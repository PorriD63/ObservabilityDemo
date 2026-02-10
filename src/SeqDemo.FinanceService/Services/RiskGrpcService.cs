using System.Diagnostics;
using Grpc.Core;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Events;
using SeqDemo.Shared.Kafka;
using SeqDemo.Shared.Protos;

namespace SeqDemo.FinanceService.Services;

/// <summary>
/// RiskService gRPC 實作 — 風險評估。
/// 對應原始 Program.cs 中 SERVICE_RISK 的業務邏輯。
/// 風險評估未通過時透過 Kafka 發送 WithdrawalFlaggedEvent。
/// </summary>
public class RiskGrpcService : RiskService.RiskServiceBase
{
    public record Dependencies(ActivitySource ActivitySource, KafkaProducer KafkaProducer);

    private readonly ActivitySource _activitySource;
    private readonly KafkaProducer _kafkaProducer;

    public RiskGrpcService(Dependencies deps)
    {
        _activitySource = deps.ActivitySource;
        _kafkaProducer = deps.KafkaProducer;
    }

    /// <summary>
    /// 風險評估 (對應 Program.cs L998-1070)
    /// 包含異常交易模式警告 (10% 機率)
    /// 風險未通過時透過 Kafka 發送 withdrawal-flagged 事件
    /// </summary>
    public override async Task<AssessRiskResponse> AssessRisk(AssessRiskRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("AssessRisk", ActivityKind.Server);
        activity?.SetTag("rpc.method", "AssessRisk");
        activity?.SetTag("rpc.service", "RiskService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "RiskAssessmentEngine");

        var riskScore = Random.Shared.Next(0, 100);
        var riskLevel = riskScore < 30 ? "Low" : riskScore < 70 ? "Medium" : "High";
        var riskPassed = riskScore < 70;

        activity?.SetTag("risk.score", riskScore);
        activity?.SetTag("risk.level", riskLevel);

        var riskAssessment = new
        {
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            Passed = riskPassed,
            Factors = new[]
            {
                "TransactionHistory",
                "AccountAge",
                "WithdrawalFrequency",
                "DepositWithdrawalRatio"
            },
            AssessedAt = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
        using (LogContext.PushProperty("SourceContext", "RiskAssessmentEngine"))
        using (LogContext.PushProperty("EventType", "RiskAssessed"))
        using (LogContext.PushProperty("RiskAssessment", riskAssessment, destructureObjects: true))
        {
            if (riskLevel == "High")
            {
                Log.Warning("高風險提款: Score {RiskScore}, Level {RiskLevel}, 需要人工審核",
                    riskScore, riskLevel);
            }
            else if (riskLevel == "Medium")
            {
                Log.Warning("中風險提款: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}",
                    riskScore, riskLevel, riskPassed);
            }
            else
            {
                Log.Information("風險評估: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}",
                    riskScore, riskLevel, riskPassed);
            }
        }

        // 異常交易模式警告 (10% 機率)
        if (Random.Shared.Next(0, 10) == 0)
        {
            var patternWarning = new
            {
                Pattern = new[] { "FrequentWithdrawals", "LargeAmountAfterDeposit", "UnusualTiming", "NewDevice" }[Random.Shared.Next(4)],
                Confidence = Random.Shared.Next(60, 95),
                RequiresReview = true
            };
            using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
            using (LogContext.PushProperty("SourceContext", "PatternDetector"))
            using (LogContext.PushProperty("PatternWarning", patternWarning, destructureObjects: true))
            {
                Log.Warning("異常交易模式: {UserId} 偵測到 {Pattern}，信心度: {Confidence}%",
                    request.UserId, patternWarning.Pattern, patternWarning.Confidence);
            }
        }

        // 風險未通過時，發送 withdrawal-flagged 事件並建立 FlaggedReview span
        if (!riskPassed)
        {
            var transactionId = Guid.NewGuid().ToString();
            var reason = new[] { "HighRiskScore", "UnusualPattern", "LargeAmount", "NewAccount" }[Random.Shared.Next(4)];
            var reviewer = $"REVIEWER_{Random.Shared.Next(1, 10)}";

            using var flagActivity = _activitySource.StartActivity("FlaggedReview", ActivityKind.Server);
            flagActivity?.SetTag("operation", "ManualReviewQueue");
            flagActivity?.SetTag("transaction.id", transactionId);

            var flagDetails = new
            {
                TransactionId = transactionId,
                Reason = reason,
                RequiresManualReview = true,
                FlaggedAt = DateTime.UtcNow,
                ReviewerAssigned = reviewer
            };

            using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
            using (LogContext.PushProperty("SourceContext", "ManualReviewQueue"))
            using (LogContext.PushProperty("EventType", "WithdrawalFlagged"))
            using (LogContext.PushProperty("TransactionId", transactionId))
            using (LogContext.PushProperty("FlagDetails", flagDetails, destructureObjects: true))
            {
                Log.Warning("提款標記審核: Transaction {TransactionId}, Reason: {Reason}, Reviewer: {ReviewerAssigned}",
                    transactionId, reason, reviewer);
            }

            // Kafka: 發送 withdrawal-flagged 事件
            await _kafkaProducer.ProduceAsync(
                KafkaTopics.WithdrawalFlagged,
                request.UserId,
                new WithdrawalFlaggedEvent(
                    request.UserId, transactionId, request.Amount,
                    reason, reviewer, DateTime.UtcNow));
        }

        await Task.Delay(200);

        return new AssessRiskResponse
        {
            RiskScore = riskScore,
            RiskLevel = riskLevel,
            Passed = riskPassed
        };
    }
}
