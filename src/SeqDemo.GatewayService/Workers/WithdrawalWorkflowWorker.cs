using System.Diagnostics;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Events;
using SeqDemo.Shared.Kafka;
using SeqDemo.Shared.Protos;

namespace SeqDemo.GatewayService.Workers;

/// <summary>
/// WithdrawalWorkflow — 3 步驟提款流程
/// Gateway → FinanceService (RequestWithdrawal)
/// Gateway → FinanceService (AssessRisk)
/// Gateway → FinanceService (ApproveWithdrawal 或 FlagReview)
/// </summary>
public class WithdrawalWorkflowWorker : BackgroundService
{
    private readonly ActivitySource _activitySource;
    private readonly WalletService.WalletServiceClient _walletClient;
    private readonly PaymentService.PaymentServiceClient _paymentClient;
    private readonly RiskService.RiskServiceClient _riskClient;
    private readonly KafkaProducer _kafkaProducer;

    public WithdrawalWorkflowWorker(
        ActivitySource activitySource,
        GrpcChannels channels,
        KafkaProducer kafkaProducer)
    {
        _activitySource = activitySource;
        _walletClient = new WalletService.WalletServiceClient(channels.FinanceChannel);
        _paymentClient = new PaymentService.PaymentServiceClient(channels.FinanceChannel);
        _riskClient = new RiskService.RiskServiceClient(channels.FinanceChannel);
        _kafkaProducer = kafkaProducer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var random = new Random();
        const string workflowName = "WithdrawalWorkflow";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, stoppingToken); // 稍微延遲開始時間

                var userId = $"USER_{random.Next(100, 999)}";
                var sessionId = Guid.NewGuid().ToString();

                // Root span — ApiGateway 作為入口
                using var workflowActivity = _activitySource.StartActivity(workflowName, ActivityKind.Server);
                workflowActivity?.SetTag("user.id", userId);
                workflowActivity?.SetTag("session.id", sessionId);
                workflowActivity?.SetTag("workflow.name", workflowName);
                workflowActivity?.SetTag("http.method", "POST");
                workflowActivity?.SetTag("http.url", "/api/withdrawal/workflow");

                using (LogContext.PushProperty("UserId", userId))
                using (LogContext.PushProperty("SessionId", sessionId))
                using (LogContext.PushProperty("WorkflowName", workflowName))
                {
                    // ═══ 步驟 1: 提款請求 ═══
                    // gRPC → FinanceService (WalletService/RequestWithdrawal)
                    var withdrawalAmount = random.Next(100, 3000);
                    var userBalance = random.Next(0, 5000);
                    var currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)];
                    var withdrawalMethod = new[] { "BankTransfer", "EWallet", "Crypto", "Check" }[random.Next(4)];
                    var accountInfo = $"****{random.Next(1000, 9999)}";

                    var withdrawalResponse = await _walletClient.RequestWithdrawalAsync(
                        new RequestWithdrawalRequest
                        {
                            UserId = userId,
                            Amount = withdrawalAmount,
                            Currency = currency,
                            WithdrawalMethod = withdrawalMethod,
                            AccountInfo = accountInfo,
                            UserBalance = userBalance
                        },
                        cancellationToken: stoppingToken);

                    if (!withdrawalResponse.Success)
                    {
                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "1-RequestWithdrawal"))
                        {
                            Log.Warning("WithdrawalWorkflow 終止: {ErrorType} - {ErrorMessage}",
                                withdrawalResponse.ErrorType, withdrawalResponse.ErrorMessage);
                        }
                        workflowActivity?.SetStatus(ActivityStatusCode.Error, withdrawalResponse.ErrorType);
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "1-RequestWithdrawal"))
                    {
                        Log.Information("步驟1完成 - 提款請求: {UserId} requests {Amount} {Currency} via {WithdrawalMethod}",
                            userId, withdrawalAmount, currency, withdrawalMethod);
                    }

                    // ═══ 步驟 2: 風險評估 ═══
                    // gRPC → FinanceService (RiskService/AssessRisk)
                    var riskResponse = await _riskClient.AssessRiskAsync(
                        new AssessRiskRequest
                        {
                            UserId = userId,
                            Amount = withdrawalAmount,
                            TransactionType = "Withdrawal"
                        },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "2-RiskAssessment"))
                    {
                        Log.Information("步驟2完成 - 風險評估: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}",
                            riskResponse.RiskScore, riskResponse.RiskLevel, riskResponse.Passed);
                    }

                    // ═══ 步驟 3: 核准或標記 ═══
                    var transactionId = Guid.NewGuid().ToString();

                    if (riskResponse.Passed)
                    {
                        // gRPC → FinanceService (PaymentService/ApproveWithdrawal)
                        var approvalResponse = await _paymentClient.ApproveWithdrawalAsync(
                            new ApproveWithdrawalRequest
                            {
                                TransactionId = transactionId,
                                UserId = userId,
                                Amount = withdrawalAmount
                            },
                            cancellationToken: stoppingToken);

                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "3-Approval"))
                        using (LogContext.PushProperty("TransactionId", transactionId))
                        {
                            Log.Information("步驟3完成 - 提款核准: Transaction {TransactionId}, ApprovedBy: {ApprovedBy}",
                                transactionId, approvalResponse.ApprovedBy);
                        }
                    }
                    else
                    {
                        // 風險未通過 → 標記人工審核 (由 FinanceService 內部處理並發送 Kafka 事件)
                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "3-FlaggedReview"))
                        using (LogContext.PushProperty("TransactionId", transactionId))
                        {
                            Log.Warning("步驟3完成 - 提款標記審核: Transaction {TransactionId}, RiskScore: {RiskScore}",
                                transactionId, riskResponse.RiskScore);
                        }
                    }

                    // Kafka publish workflow-completed
                    await _kafkaProducer.ProduceAsync(
                        KafkaTopics.WorkflowCompleted,
                        userId,
                        new WorkflowCompletedEvent(userId, workflowName, sessionId, DateTime.UtcNow),
                        stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("SourceContext", "WorkflowOrchestrator"))
                    {
                        Log.Information("WithdrawalWorkflow completed for {UserId}", userId);
                    }
                }

                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in WithdrawalWorkflow");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
