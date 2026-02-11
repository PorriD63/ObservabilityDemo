using System.Diagnostics;
using Grpc.Core;
using Serilog;
using Serilog.Context;
using ObservabilityDemo.Shared.Constants;
using ObservabilityDemo.Shared.Events;
using ObservabilityDemo.Shared.Telemetry;
using ObservabilityDemo.Shared.Kafka;
using ObservabilityDemo.Shared.Protos;

namespace ObservabilityDemo.FinanceService.Services;

/// <summary>
/// PaymentService gRPC 實作 — 支付驗證、支付處理、提款核准。
/// 對應原始 Program.cs 中 SERVICE_PAYMENT 的所有業務邏輯。
/// </summary>
public class PaymentGrpcService : PaymentService.PaymentServiceBase
{
    public record Dependencies(ActivitySource ActivitySource, KafkaProducer KafkaProducer);

    private readonly ActivitySource _activitySource;
    private readonly KafkaProducer _kafkaProducer;

    public PaymentGrpcService(Dependencies deps)
    {
        _activitySource = deps.ActivitySource;
        _kafkaProducer = deps.KafkaProducer;
    }

    /// <summary>
    /// 驗證支付方式 (對應 Program.cs L648-702)
    /// 95% 成功率
    /// </summary>
    public override async Task<ValidatePaymentResponse> ValidatePayment(ValidatePaymentRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("ValidatePayment", context);
        activity?.SetTag("rpc.method", "ValidatePayment");
        activity?.SetTag("rpc.service", "PaymentService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "PaymentValidator");

        var isValid = Random.Shared.Next(0, 100) > 5; // 95% 成功率
        var processorId = $"PROC_{Random.Shared.Next(1, 10)}";
        var failureReason = isValid
            ? ""
            : new[] { "KYC_NOT_VERIFIED", "PAYMENT_METHOD_SUSPENDED", "ACCOUNT_RESTRICTED" }[Random.Shared.Next(3)];

        var validationDetails = new
        {
            IsValid = isValid,
            ValidationRules = new[] { "AmountLimit", "PaymentMethodActive", "KYCVerified" },
            ProcessorId = processorId,
            ValidatedAt = DateTime.UtcNow,
            FailureReason = isValid ? (string?)null : failureReason
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.PaymentService))
        using (LogContext.PushProperty("SourceContext", "PaymentValidator"))
        using (LogContext.PushProperty("EventType", isValid ? "PaymentValidated" : "PaymentValidationFailed"))
        using (LogContext.PushProperty("ValidationDetails", validationDetails, destructureObjects: true))
        {
            if (isValid)
            {
                Log.Information("驗證支付方式: {PaymentMethod} is valid, Processor: {ProcessorId}",
                    request.PaymentMethod, processorId);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Payment validation failed");
                Log.Error("驗證失敗: {UserId} 的支付方式 {PaymentMethod} 驗證失敗，原因: {FailureReason}",
                    request.UserId, request.PaymentMethod, failureReason);
            }
        }

        await Task.Delay(200);

        return new ValidatePaymentResponse
        {
            IsValid = isValid,
            ProcessorId = processorId,
            FailureReason = failureReason
        };
    }

    /// <summary>
    /// 處理支付 (對應 Program.cs L704-805)
    /// 90% 成功率，8% 連線問題警告
    /// 成功後透過 Kafka 發送 PaymentProcessedEvent
    /// </summary>
    public override async Task<ProcessPaymentResponse> ProcessPayment(ProcessPaymentRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("ProcessPayment", context);
        activity?.SetTag("rpc.method", "ProcessPayment");
        activity?.SetTag("rpc.service", "PaymentService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "PaymentProcessor");

        var transactionId = Guid.NewGuid().ToString();
        activity?.SetTag("transaction.id", transactionId);

        var isSuccess = Random.Shared.Next(0, 10) > 0; // 90% 成功率
        var fee = request.Amount * 0.02; // 2% 手續費
        var netAmount = request.Amount - fee;

        // 支付處理器連線問題 (8% 機率)
        if (Random.Shared.Next(0, 100) < 8)
        {
            var connectionError = new
            {
                ProcessorId = request.ProcessorId,
                ErrorType = "ConnectionTimeout",
                RetryCount = Random.Shared.Next(1, 4),
                Timestamp = DateTime.UtcNow
            };
            using (LogContext.PushProperty("ServiceName", ServiceNames.PaymentService))
            using (LogContext.PushProperty("SourceContext", "PaymentGatewayClient"))
            using (LogContext.PushProperty("EventType", "PaymentProcessorConnectionError"))
            using (LogContext.PushProperty("TransactionId", transactionId))
            using (LogContext.PushProperty("ConnectionError", connectionError, destructureObjects: true))
            {
                Log.Warning("支付處理器連線問題: Processor {ProcessorId} 連線超時，重試次數: {RetryCount}",
                    request.ProcessorId, connectionError.RetryCount);
            }
        }

        var errorCode = isSuccess
            ? ""
            : new[] { "INSUFFICIENT_FUNDS", "CARD_DECLINED", "BANK_REJECTION", "FRAUD_DETECTED" }[Random.Shared.Next(4)];

        var paymentDetails = new
        {
            TransactionId = transactionId,
            Status = isSuccess ? "Success" : "Failed",
            Amount = request.Amount,
            Fee = fee,
            NetAmount = netAmount,
            ProcessedAt = DateTime.UtcNow,
            ErrorCode = isSuccess ? (string?)null : errorCode
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.PaymentService))
        using (LogContext.PushProperty("SourceContext", "PaymentProcessor"))
        using (LogContext.PushProperty("EventType", isSuccess ? "PaymentProcessed" : "PaymentFailed"))
        using (LogContext.PushProperty("TransactionId", transactionId))
        using (LogContext.PushProperty("PaymentDetails", paymentDetails, destructureObjects: true))
        {
            if (isSuccess)
            {
                Log.Information("處理支付成功: Transaction {TransactionId}, Amount: {Amount}, Fee: {Fee}",
                    transactionId, request.Amount, fee);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Payment processing failed");
                Log.Error("處理支付失敗: Transaction {TransactionId}, Error: {ErrorCode}",
                    transactionId, errorCode);
            }
        }

        // Kafka: 發送 payment-processed 事件
        await _kafkaProducer.ProduceAsync(
            KafkaTopics.PaymentProcessed,
            request.UserId,
            new PaymentProcessedEvent(
                request.UserId, transactionId, request.Amount,
                fee, netAmount,
                isSuccess ? "Success" : "Failed",
                isSuccess ? null : errorCode,
                DateTime.UtcNow));

        await Task.Delay(100);

        return new ProcessPaymentResponse
        {
            Success = isSuccess,
            TransactionId = transactionId,
            Fee = fee,
            NetAmount = netAmount,
            ErrorCode = errorCode
        };
    }

    /// <summary>
    /// 核准提款 (對應 Program.cs L1077-1107)
    /// 核准後透過 Kafka 發送 WithdrawalApprovedEvent
    /// </summary>
    public override async Task<ApproveWithdrawalResponse> ApproveWithdrawal(ApproveWithdrawalRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("ApproveWithdrawal", context);
        activity?.SetTag("rpc.method", "ApproveWithdrawal");
        activity?.SetTag("rpc.service", "PaymentService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "WithdrawalApprover");
        activity?.SetTag("transaction.id", request.TransactionId);

        var approvalDetails = new
        {
            TransactionId = request.TransactionId,
            ApprovedBy = "AutomatedSystem",
            ApprovedAt = DateTime.UtcNow,
            EstimatedCompletionTime = DateTime.UtcNow.AddHours(24)
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.PaymentService))
        using (LogContext.PushProperty("SourceContext", "WithdrawalApprover"))
        using (LogContext.PushProperty("EventType", "WithdrawalApproved"))
        using (LogContext.PushProperty("TransactionId", request.TransactionId))
        using (LogContext.PushProperty("ApprovalDetails", approvalDetails, destructureObjects: true))
        {
            Log.Information("提款核准: Transaction {TransactionId}, Estimated completion in 24h",
                request.TransactionId);
        }

        // Kafka: 發送 withdrawal-approved 事件
        await _kafkaProducer.ProduceAsync(
            KafkaTopics.WithdrawalApproved,
            request.UserId,
            new WithdrawalApprovedEvent(
                request.UserId, request.TransactionId, request.Amount,
                "USD", "AutomatedSystem", DateTime.UtcNow));

        await Task.Delay(100);

        return new ApproveWithdrawalResponse
        {
            Approved = true,
            ApprovedBy = "AutomatedSystem"
        };
    }
}
