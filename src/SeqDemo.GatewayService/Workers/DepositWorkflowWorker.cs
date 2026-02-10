using System.Diagnostics;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Events;
using SeqDemo.Shared.Kafka;
using SeqDemo.Shared.Protos;

namespace SeqDemo.GatewayService.Workers;

/// <summary>
/// DepositWorkflow — 4 步驟存款流程
/// Gateway → FinanceService (InitiateDeposit)
/// Gateway → FinanceService (ValidatePayment, ProcessPayment)
/// Gateway → FinanceService (CreditBalance)
/// </summary>
public class DepositWorkflowWorker : BackgroundService
{
    private readonly ActivitySource _activitySource;
    private readonly WalletService.WalletServiceClient _walletClient;
    private readonly PaymentService.PaymentServiceClient _paymentClient;
    private readonly KafkaProducer _kafkaProducer;

    public DepositWorkflowWorker(
        ActivitySource activitySource,
        GrpcChannels channels,
        KafkaProducer kafkaProducer)
    {
        _activitySource = activitySource;
        _walletClient = new WalletService.WalletServiceClient(channels.FinanceChannel);
        _paymentClient = new PaymentService.PaymentServiceClient(channels.FinanceChannel);
        _kafkaProducer = kafkaProducer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var random = new Random();
        const string workflowName = "DepositWorkflow";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(500, stoppingToken); // 稍微延遲開始時間

                var userId = $"USER_{random.Next(100, 999)}";
                var sessionId = Guid.NewGuid().ToString();

                // Root span — ApiGateway 作為入口
                using var workflowActivity = _activitySource.StartActivity(workflowName, ActivityKind.Server);
                workflowActivity?.SetTag("user.id", userId);
                workflowActivity?.SetTag("session.id", sessionId);
                workflowActivity?.SetTag("workflow.name", workflowName);
                workflowActivity?.SetTag("http.method", "POST");
                workflowActivity?.SetTag("http.url", "/api/deposit/workflow");

                using (LogContext.PushProperty("UserId", userId))
                using (LogContext.PushProperty("SessionId", sessionId))
                using (LogContext.PushProperty("WorkflowName", workflowName))
                {
                    // ═══ 步驟 1: 發起存款 ═══
                    // gRPC → FinanceService (WalletService/InitiateDeposit)
                    var depositAmount = random.Next(100, 5000);
                    var currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)];
                    var paymentMethod = new[] { "CreditCard", "BankTransfer", "EWallet", "Crypto" }[random.Next(4)];

                    var depositResponse = await _walletClient.InitiateDepositAsync(
                        new InitiateDepositRequest
                        {
                            UserId = userId,
                            Amount = depositAmount,
                            Currency = currency,
                            PaymentMethod = paymentMethod
                        },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "1-InitiateDeposit"))
                    {
                        Log.Information("步驟1完成 - 發起存款: {UserId} requests {Amount} {Currency} via {PaymentMethod}",
                            userId, depositAmount, currency, paymentMethod);
                    }

                    // ═══ 步驟 2: 驗證支付方式 ═══
                    // gRPC → FinanceService (PaymentService/ValidatePayment)
                    var validateResponse = await _paymentClient.ValidatePaymentAsync(
                        new ValidatePaymentRequest
                        {
                            UserId = userId,
                            Amount = depositAmount,
                            PaymentMethod = paymentMethod
                        },
                        cancellationToken: stoppingToken);

                    if (!validateResponse.IsValid)
                    {
                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "2-ValidatePayment"))
                        {
                            Log.Warning("DepositWorkflow 因驗證失敗而終止: {UserId}, Reason: {FailureReason}",
                                userId, validateResponse.FailureReason);
                        }
                        workflowActivity?.SetStatus(ActivityStatusCode.Error, "Payment validation failed");
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "2-ValidatePayment"))
                    {
                        Log.Information("步驟2完成 - 驗證支付方式: {PaymentMethod} is valid, Processor: {ProcessorId}",
                            paymentMethod, validateResponse.ProcessorId);
                    }

                    // ═══ 步驟 3: 處理支付 ═══
                    // gRPC → FinanceService (PaymentService/ProcessPayment)
                    var processResponse = await _paymentClient.ProcessPaymentAsync(
                        new ProcessPaymentRequest
                        {
                            UserId = userId,
                            Amount = depositAmount,
                            PaymentMethod = paymentMethod,
                            ProcessorId = validateResponse.ProcessorId
                        },
                        cancellationToken: stoppingToken);

                    if (!processResponse.Success)
                    {
                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "3-ProcessPayment"))
                        {
                            Log.Warning("DepositWorkflow 支付處理失敗: {UserId}, Error: {ErrorCode}",
                                userId, processResponse.ErrorCode);
                        }
                        workflowActivity?.SetStatus(ActivityStatusCode.Error, "Payment processing failed");

                        // 即使支付失敗也發送 workflow-completed (帶失敗狀態)
                        await _kafkaProducer.ProduceAsync(
                            KafkaTopics.WorkflowCompleted,
                            userId,
                            new WorkflowCompletedEvent(userId, workflowName, sessionId, DateTime.UtcNow),
                            stoppingToken);

                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "3-ProcessPayment"))
                    using (LogContext.PushProperty("TransactionId", processResponse.TransactionId))
                    {
                        Log.Information("步驟3完成 - 處理支付: Transaction {TransactionId}, Amount: {Amount}, Fee: {Fee}",
                            processResponse.TransactionId, depositAmount, processResponse.Fee);
                    }

                    // ═══ 步驟 4: 餘額入帳 ═══
                    // gRPC → FinanceService (WalletService/CreditBalance)
                    var creditResponse = await _walletClient.CreditBalanceAsync(
                        new CreditBalanceRequest
                        {
                            UserId = userId,
                            Amount = processResponse.NetAmount,
                            TransactionId = processResponse.TransactionId
                        },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "4-CreditBalance"))
                    {
                        Log.Information("步驟4完成 - 餘額入帳: {NetAmount} credited, New Balance: {NewBalance}",
                            processResponse.NetAmount, creditResponse.NewBalance);
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
                        Log.Information("DepositWorkflow completed for {UserId}", userId);
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
                Log.Error(ex, "Error in DepositWorkflow");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
