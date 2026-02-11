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
/// WalletService gRPC 實作 — 餘額查詢、結算、餘額更新、存款、入帳、提款請求。
/// 對應原始 Program.cs 中 SERVICE_WALLET 的所有業務邏輯。
/// </summary>
public class WalletGrpcService : WalletService.WalletServiceBase
{
    public record Dependencies(ActivitySource ActivitySource, KafkaProducer KafkaProducer);

    private readonly ActivitySource _activitySource;
    private readonly KafkaProducer _kafkaProducer;

    public WalletGrpcService(Dependencies deps)
    {
        _activitySource = deps.ActivitySource;
        _kafkaProducer = deps.KafkaProducer;
    }

    /// <summary>
    /// 查詢餘額 (對應 Program.cs L200-242)
    /// </summary>
    public override async Task<GetBalanceResponse> GetBalance(GetBalanceRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("GetBalance", context);
        activity?.SetTag("rpc.method", "GetBalance");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");

        var balance = Random.Shared.Next(100, 10000);
        var currency = new[] { "USD", "TWD", "HKD", "SGD" }[Random.Shared.Next(4)];
        var walletId = $"WALLET_{Random.Shared.Next(1000, 9999)}";

        activity?.SetTag("wallet.balance", balance);
        activity?.SetTag("wallet.currency", currency);

        var balanceInfo = new
        {
            Balance = balance,
            Currency = currency,
            WalletId = walletId,
            LastUpdated = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "BalanceManager"))
        using (LogContext.PushProperty("EventType", "BalanceChecked"))
        using (LogContext.PushProperty("BalanceInfo", balanceInfo, destructureObjects: true))
        {
            Log.Information("查詢餘額: {UserId} Balance: {Balance} {Currency}",
                request.UserId, balance, currency);

            // 低餘額警告 (20% 機率)
            if (balance < 500 && Random.Shared.Next(0, 5) == 0)
            {
                activity?.SetStatus(ActivityStatusCode.Ok, "Low balance warning");
                Log.Warning("低餘額警告: {UserId} 餘額僅剩 {Balance} {Currency}",
                    request.UserId, balance, currency);
            }
        }

        await Task.Delay(100);

        return new GetBalanceResponse
        {
            Balance = balance,
            Currency = currency,
            WalletId = walletId
        };
    }

    /// <summary>
    /// 注單結算 (對應 Program.cs L440-494)
    /// 結算後透過 Kafka 發送 BetSettledEvent
    /// </summary>
    public override async Task<SettlementResponse> Settlement(SettlementRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("Settlement", context);
        activity?.SetTag("rpc.method", "Settlement");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");

        var isWin = Random.Shared.Next(0, 2) == 1;
        var winAmount = isWin ? request.BetAmount * Random.Shared.Next(2, 5) : 0;
        var profit = winAmount - request.BetAmount;
        var transactionId = Guid.NewGuid().ToString();

        activity?.SetTag("transaction.id", transactionId);
        activity?.SetTag("bet.result", isWin ? "Win" : "Loss");
        activity?.SetTag("bet.profit", profit);

        var settlementDetails = new
        {
            BetId = request.BetId,
            TransactionId = transactionId,
            BetAmount = request.BetAmount,
            WinAmount = winAmount,
            Profit = profit,
            Status = isWin ? "Win" : "Loss",
            SettledAt = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "SettlementProcessor"))
        using (LogContext.PushProperty("EventType", "BetSettled"))
        using (LogContext.PushProperty("TransactionId", transactionId))
        using (LogContext.PushProperty("SettlementDetails", settlementDetails, destructureObjects: true))
        {
            Log.Information("注單結算: {Status}, Bet: {BetAmount}, Win: {WinAmount}, Profit: {Profit}",
                settlementDetails.Status, request.BetAmount, winAmount, profit);

            // 結算延遲警告 (10% 機率)
            if (Random.Shared.Next(0, 10) == 0)
            {
                var delayInfo = new
                {
                    TransactionId = transactionId,
                    DelaySeconds = Random.Shared.Next(3, 10),
                    Reason = new[] { "DatabaseLatency", "HighLoad", "NetworkIssue" }[Random.Shared.Next(3)]
                };
                using (LogContext.PushProperty("DelayInfo", delayInfo, destructureObjects: true))
                {
                    Log.Warning("結算延遲: Transaction {TransactionId} 延遲 {DelaySeconds} 秒，原因: {Reason}",
                        transactionId, delayInfo.DelaySeconds, delayInfo.Reason);
                }
            }
        }

        // Kafka: 發送 bet-settled 事件
        await _kafkaProducer.ProduceAsync(
            KafkaTopics.BetSettled,
            request.UserId,
            new BetSettledEvent(
                request.UserId, request.BetId, transactionId,
                request.BetAmount, winAmount, profit,
                isWin ? "Win" : "Loss", DateTime.UtcNow));

        await Task.Delay(100);

        return new SettlementResponse
        {
            IsWin = isWin,
            WinAmount = winAmount,
            Profit = profit,
            TransactionId = transactionId
        };
    }

    /// <summary>
    /// 餘額更新 (對應 Program.cs L497-544)
    /// </summary>
    public override async Task<UpdateBalanceResponse> UpdateBalance(UpdateBalanceRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("UpdateBalance", context);
        activity?.SetTag("rpc.method", "UpdateBalance");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");

        var newBalance = request.CurrentBalance + request.Profit;
        activity?.SetTag("wallet.previous_balance", request.CurrentBalance);
        activity?.SetTag("wallet.new_balance", newBalance);

        var balanceChange = new
        {
            PreviousBalance = request.CurrentBalance,
            NewBalance = newBalance,
            ChangeAmount = request.Profit,
            ChangeType = request.Profit >= 0 ? "Credit" : "Debit",
            TransactionId = request.TransactionId,
            Timestamp = DateTime.UtcNow
        };

        // 餘額更新錯誤 (5% 機率)
        var hasError = Random.Shared.Next(0, 20) == 0;

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "BalanceManager"))
        using (LogContext.PushProperty("EventType", "BalanceUpdated"))
        using (LogContext.PushProperty("BalanceChange", balanceChange, destructureObjects: true))
        {
            Log.Information("餘額更新: {PreviousBalance} -> {NewBalance} ({ChangeType}: {ChangeAmount})",
                request.CurrentBalance, newBalance, balanceChange.ChangeType, Math.Abs(request.Profit));

            if (hasError)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Database write failed");
                var updateError = new
                {
                    TransactionId = request.TransactionId,
                    ErrorCode = "BALANCE_UPDATE_FAILED",
                    ErrorMessage = "資料庫寫入失敗，將重試",
                    RetryAttempt = 1,
                    MaxRetries = 3
                };
                using (LogContext.PushProperty("UpdateError", updateError, destructureObjects: true))
                {
                    Log.Error("餘額更新失敗: Transaction {TransactionId}，錯誤: {ErrorMessage}",
                        request.TransactionId, updateError.ErrorMessage);
                }
            }
        }

        await Task.Delay(100);

        return new UpdateBalanceResponse
        {
            PreviousBalance = request.CurrentBalance,
            NewBalance = newBalance,
            Success = !hasError,
            ErrorMessage = hasError ? "資料庫寫入失敗，將重試" : ""
        };
    }

    /// <summary>
    /// 發起存款 (對應 Program.cs L596-646)
    /// </summary>
    public override async Task<InitiateDepositResponse> InitiateDeposit(InitiateDepositRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("InitiateDeposit", context);
        activity?.SetTag("rpc.method", "InitiateDeposit");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "DepositRequestHandler");

        var depositId = Guid.NewGuid().ToString();

        var depositRequest = new
        {
            Amount = request.Amount,
            Currency = request.Currency,
            PaymentMethod = request.PaymentMethod,
            RequestedAt = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "DepositRequestHandler"))
        using (LogContext.PushProperty("EventType", "DepositInitiated"))
        using (LogContext.PushProperty("DepositRequest", depositRequest, destructureObjects: true))
        {
            Log.Information("發起存款: {UserId} requests {Amount} {Currency} via {PaymentMethod}",
                request.UserId, request.Amount, request.Currency, request.PaymentMethod);

            // 金額超過限制警告 (15% 機率) - RiskService 檢查
            if (request.Amount > 3000 && Random.Shared.Next(0, 100) < 15)
            {
                using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
                using (LogContext.PushProperty("SourceContext", "TransactionLimitChecker"))
                {
                    var limitWarning = new
                    {
                        Amount = request.Amount,
                        DailyLimit = 10000,
                        SingleTransactionLimit = 5000,
                        RequiresAdditionalVerification = request.Amount > 5000
                    };
                    using (LogContext.PushProperty("LimitWarning", limitWarning, destructureObjects: true))
                    {
                        Log.Warning("存款金額警告: {UserId} 存款 {Amount} 接近限額，需要額外驗證: {RequiresAdditionalVerification}",
                            request.UserId, request.Amount, limitWarning.RequiresAdditionalVerification);
                    }
                }
            }
        }

        await Task.Delay(100);

        return new InitiateDepositResponse
        {
            Success = true,
            DepositId = depositId
        };
    }

    /// <summary>
    /// 餘額入帳 (對應 Program.cs L773-805)
    /// </summary>
    public override async Task<CreditBalanceResponse> CreditBalance(CreditBalanceRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("CreditBalance", context);
        activity?.SetTag("rpc.method", "CreditBalance");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "BalanceManager");

        var newBalance = Random.Shared.Next(1000, 20000);
        activity?.SetTag("wallet.new_balance", newBalance);

        var creditDetails = new
        {
            Amount = request.Amount,
            TransactionId = request.TransactionId,
            NewBalance = newBalance,
            CreditedAt = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "BalanceManager"))
        using (LogContext.PushProperty("EventType", "BalanceCredited"))
        using (LogContext.PushProperty("CreditDetails", creditDetails, destructureObjects: true))
        {
            Log.Information("餘額入帳: {Amount} credited, New Balance: {NewBalance}",
                request.Amount, newBalance);
        }

        await Task.Delay(100);

        return new CreditBalanceResponse
        {
            NewBalance = newBalance
        };
    }

    /// <summary>
    /// 提款請求 (對應 Program.cs L858-996)
    /// 包含餘額不足、帳戶凍結、KYC 警告、每日限額警告等情境
    /// </summary>
    public override async Task<RequestWithdrawalResponse> RequestWithdrawal(RequestWithdrawalRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartGrpcServerActivity("RequestWithdrawal", context);
        activity?.SetTag("rpc.method", "RequestWithdrawal");
        activity?.SetTag("rpc.service", "WalletService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "WithdrawalRequestHandler");

        // 餘額不足錯誤 (12% 機率)
        if (request.UserBalance < request.Amount && Random.Shared.Next(0, 100) < 12)
        {
            var insufficientBalanceError = new
            {
                RequestedAmount = request.Amount,
                AvailableBalance = request.UserBalance,
                Shortage = request.Amount - request.UserBalance,
                Currency = request.Currency
            };

            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient balance");

            using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
            using (LogContext.PushProperty("SourceContext", "BalanceValidator"))
            using (LogContext.PushProperty("EventType", "WithdrawalRejectedInsufficientBalance"))
            using (LogContext.PushProperty("InsufficientBalanceError", insufficientBalanceError, destructureObjects: true))
            {
                Log.Error("提款失敗 - 餘額不足: {UserId} 請求提款 {RequestedAmount}，但餘額僅 {AvailableBalance}",
                    request.UserId, request.Amount, request.UserBalance);
            }

            return new RequestWithdrawalResponse
            {
                Success = false,
                ErrorType = "InsufficientBalance",
                ErrorMessage = $"餘額不足: 請求 {request.Amount}，可用 {request.UserBalance}"
            };
        }

        // 帳戶凍結錯誤 (5% 機率)
        if (Random.Shared.Next(0, 100) < 5)
        {
            var reason = new[] { "SUSPECTED_FRAUD", "PENDING_INVESTIGATION", "COMPLIANCE_HOLD", "DUPLICATE_ACCOUNT" }[Random.Shared.Next(4)];
            var accountFrozenError = new
            {
                Reason = reason,
                FrozenSince = DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 30)),
                ContactSupport = true
            };

            activity?.SetStatus(ActivityStatusCode.Error, "Account frozen");

            using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
            using (LogContext.PushProperty("SourceContext", "AccountStatusChecker"))
            using (LogContext.PushProperty("EventType", "WithdrawalRejectedAccountFrozen"))
            using (LogContext.PushProperty("AccountFrozenError", accountFrozenError, destructureObjects: true))
            {
                Log.Error("提款失敗 - 帳戶凍結: {UserId} 帳戶已凍結，原因: {Reason}",
                    request.UserId, reason);
            }

            return new RequestWithdrawalResponse
            {
                Success = false,
                ErrorType = "AccountFrozen",
                ErrorMessage = $"帳戶已凍結，原因: {reason}"
            };
        }

        // 正常流程
        var withdrawalId = Guid.NewGuid().ToString();
        var withdrawalRequest = new
        {
            Amount = request.Amount,
            Currency = request.Currency,
            WithdrawalMethod = request.WithdrawalMethod,
            AccountInfo = request.AccountInfo,
            RequestedAt = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
        using (LogContext.PushProperty("SourceContext", "WithdrawalRequestHandler"))
        using (LogContext.PushProperty("EventType", "WithdrawalRequested"))
        using (LogContext.PushProperty("WithdrawalRequest", withdrawalRequest, destructureObjects: true))
        {
            Log.Information("提款請求: {UserId} requests {Amount} {Currency} via {WithdrawalMethod}",
                request.UserId, request.Amount, request.Currency, request.WithdrawalMethod);
        }

        // KYC 未完成警告 (10% 機率)
        if (Random.Shared.Next(0, 10) == 0)
        {
            var kycWarning = new
            {
                KYCStatus = "Incomplete",
                MissingDocuments = new[] { "ID Verification", "Address Proof" },
                RequiredForAmount = request.Amount > 1000
            };
            using (LogContext.PushProperty("ServiceName", ServiceNames.PlayerService))
            using (LogContext.PushProperty("SourceContext", "KYCValidator"))
            using (LogContext.PushProperty("KYCWarning", kycWarning, destructureObjects: true))
            {
                Log.Warning("KYC 警告: {UserId} KYC 未完成，缺少文件，大額提款需要完成 KYC",
                    request.UserId);
            }
        }

        // 超過每日提款限制警告 (8% 機率)
        if (request.Amount > 2000 && Random.Shared.Next(0, 100) < 8)
        {
            var todayWithdrawn = Random.Shared.Next(1000, 3000);
            var dailyLimitWarning = new
            {
                RequestedAmount = request.Amount,
                DailyLimit = 5000,
                TodayWithdrawn = todayWithdrawn,
                RemainingLimit = 5000 - todayWithdrawn
            };
            using (LogContext.PushProperty("ServiceName", ServiceNames.RiskService))
            using (LogContext.PushProperty("SourceContext", "TransactionLimitChecker"))
            using (LogContext.PushProperty("DailyLimitWarning", dailyLimitWarning, destructureObjects: true))
            {
                Log.Warning("每日提款限額警告: {UserId} 請求 {RequestedAmount}，今日已提款 {TodayWithdrawn}，剩餘額度 {RemainingLimit}",
                    request.UserId, request.Amount, dailyLimitWarning.TodayWithdrawn, dailyLimitWarning.RemainingLimit);
            }
        }

        await Task.Delay(150);

        return new RequestWithdrawalResponse
        {
            Success = true,
            WithdrawalId = withdrawalId
        };
    }
}
