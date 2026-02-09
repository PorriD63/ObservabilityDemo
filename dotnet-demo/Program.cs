using System.Diagnostics;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

// 定義微服務名稱
const string SERVICE_NAME = "DotnetSeqDemo";
const string SERVICE_PLAYER = "PlayerService";
const string SERVICE_GAME = "GameService";
const string SERVICE_WALLET = "WalletService";
const string SERVICE_PAYMENT = "PaymentService";
const string SERVICE_RISK = "RiskService";
const string SERVICE_NOTIFICATION = "NotificationService";

// 創建 ActivitySource 用於 Tracing
var activitySource = new ActivitySource(SERVICE_NAME);

// 配置 OpenTelemetry Tracing
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService(serviceName: SERVICE_NAME, serviceNamespace: "Demo", serviceVersion: "1.0.0")
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment"] = "Demo"
        }))
    .AddSource(SERVICE_NAME)
    .AddOtlpExporter(options =>
    {
        options.Endpoint = new Uri("http://localhost:4317");
    })
    .Build();

// 配置 Serilog - 透過 OpenTelemetry Collector 統一發送到 Seq, Loki, Elasticsearch
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "DotnetSeqDemo")
    .Enrich.WithProperty("Environment", "Demo")
    .Enrich.With(new ActivityEnricher())  // 自動加入 TraceId, SpanId
    // Console 輸出
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{ServiceName}/{SourceContext}] [TraceId:{TraceId}] {Message:lj}{NewLine}{Exception}")
    // OpenTelemetry Collector - 統一收集後分發到 Seq, Loki, Elasticsearch, Tempo
    .WriteTo.OpenTelemetry(options =>
    {
        options.Endpoint = "http://localhost:4317";
        options.Protocol = OtlpProtocol.Grpc;
        options.ResourceAttributes = new Dictionary<string, object>
        {
            ["service.name"] = "DotnetSeqDemo",
            ["service.namespace"] = "Demo",
            ["deployment.environment"] = "Demo"
        };
    })
    .CreateLogger();

try
{
    Log.Information("=== .NET Seq Demo Started ===");
    Log.Information("模擬微服務: {Services}", new[] { SERVICE_PLAYER, SERVICE_GAME, SERVICE_WALLET, SERVICE_PAYMENT, SERVICE_RISK, SERVICE_NOTIFICATION });
    Log.Information("Press Ctrl+C to stop");

    // 啟動三個 workflow 的定時執行
    var cancellationTokenSource = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, e) =>
    {
        e.Cancel = true;
        cancellationTokenSource.Cancel();
        Log.Information("Shutting down gracefully...");
    };

    var tasks = new List<Task>
    {
        Task.Run(() => RunBettingWorkflow(cancellationTokenSource.Token)),
        Task.Run(() => RunDepositWorkflow(cancellationTokenSource.Token)),
        Task.Run(() => RunWithdrawalWorkflow(cancellationTokenSource.Token))
    };

    await Task.WhenAll(tasks);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

// Workflow 1: 下注流程 (BettingWorkflow) - 8 步驟
// 模擬跨服務調用: PlayerService -> WalletService -> GameService -> WalletService
async Task RunBettingWorkflow(CancellationToken cancellationToken)
{
    var random = new Random();
    var workflowName = "BettingWorkflow";

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            var userId = $"USER_{random.Next(100, 999)}";
            var sessionId = Guid.NewGuid().ToString();

            // 創建 Workflow Span - TraceId 會自動生成並關聯到所有 logs
            using var workflowActivity = activitySource.StartActivity(workflowName, ActivityKind.Internal);
            workflowActivity?.SetTag("user.id", userId);
            workflowActivity?.SetTag("session.id", sessionId);
            workflowActivity?.SetTag("workflow.name", workflowName);

            using (LogContext.PushProperty("UserId", userId))
            using (LogContext.PushProperty("SessionId", sessionId))
            using (LogContext.PushProperty("WorkflowName", workflowName))
            {
                // 步驟 1: 玩家登入 (PlayerService.AuthenticationHandler)
                using (var stepActivity = activitySource.StartActivity("1-Login", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_PLAYER);
                    stepActivity?.SetTag("operation", "AuthenticationHandler");

                    var loginContext = new
                    {
                        IP = $"{random.Next(1, 255)}.{random.Next(1, 255)}.{random.Next(1, 255)}.{random.Next(1, 255)}",
                        Device = new[] { "iOS", "Android", "Desktop", "Mobile Web" }[random.Next(4)],
                        Browser = new[] { "Chrome", "Safari", "Firefox", "Edge" }[random.Next(4)],
                        Location = new[] { "台灣", "香港", "新加坡", "日本" }[random.Next(4)]
                    };

                    stepActivity?.SetTag("user.ip", loginContext.IP);
                    stepActivity?.SetTag("user.device", loginContext.Device);
                    stepActivity?.SetTag("user.location", loginContext.Location);

                    using (LogContext.PushProperty("ServiceName", SERVICE_PLAYER))
                    using (LogContext.PushProperty("SourceContext", "AuthenticationHandler"))
                    using (LogContext.PushProperty("WorkflowStep", "1-Login"))
                    using (LogContext.PushProperty("EventType", "PlayerLogin"))
                    using (LogContext.PushProperty("LoginContext", loginContext, destructureObjects: true))
                    {
                        Log.Information("玩家登入: {UserId} from {Location} using {Device}",
                            userId, loginContext.Location, loginContext.Device);
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 步驟 2: 玩家驗證 (PlayerService.AuthorizationHandler)
                using (var stepActivity = activitySource.StartActivity("2-Authentication", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_PLAYER);
                    stepActivity?.SetTag("operation", "AuthorizationHandler");

                    var authDetails = new
                    {
                        AuthMethod = new[] { "Password", "Biometric", "2FA", "OAuth" }[random.Next(4)],
                        Token = Guid.NewGuid().ToString(),
                        Role = new[] { "Player", "VIP", "Premium" }[random.Next(3)],
                        AuthTimestamp = DateTime.UtcNow
                    };

                    stepActivity?.SetTag("auth.method", authDetails.AuthMethod);
                    stepActivity?.SetTag("user.role", authDetails.Role);

                    using (LogContext.PushProperty("ServiceName", SERVICE_PLAYER))
                    using (LogContext.PushProperty("SourceContext", "AuthorizationHandler"))
                    using (LogContext.PushProperty("WorkflowStep", "2-Authentication"))
                    using (LogContext.PushProperty("EventType", "PlayerAuthenticated"))
                    using (LogContext.PushProperty("AuthDetails", authDetails, destructureObjects: true))
                    {
                        Log.Information("玩家驗證成功: {UserId} with {AuthMethod}, Role: {Role}",
                            userId, authDetails.AuthMethod, authDetails.Role);
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 步驟 3: 查詢餘額 (WalletService.BalanceManager)
                var currentBalance = random.Next(100, 10000);
                string balanceCurrency;
                using (var stepActivity = activitySource.StartActivity("3-BalanceCheck", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_WALLET);
                    stepActivity?.SetTag("operation", "BalanceManager");

                    var balanceInfo = new
                    {
                        Balance = currentBalance,
                        Currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)],
                        WalletId = $"WALLET_{random.Next(1000, 9999)}",
                        LastUpdated = DateTime.UtcNow
                    };
                    balanceCurrency = balanceInfo.Currency;

                    stepActivity?.SetTag("wallet.balance", currentBalance);
                    stepActivity?.SetTag("wallet.currency", balanceInfo.Currency);

                    using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                    using (LogContext.PushProperty("SourceContext", "BalanceManager"))
                    using (LogContext.PushProperty("WorkflowStep", "3-BalanceCheck"))
                    using (LogContext.PushProperty("EventType", "BalanceChecked"))
                    using (LogContext.PushProperty("BalanceInfo", balanceInfo, destructureObjects: true))
                    {
                        Log.Information("查詢餘額: {UserId} Balance: {Balance} {Currency}",
                            userId, balanceInfo.Balance, balanceInfo.Currency);

                        // 低餘額警告 (20% 機率)
                        if (currentBalance < 500 && random.Next(0, 5) == 0)
                        {
                            stepActivity?.SetStatus(ActivityStatusCode.Ok, "Low balance warning");
                            Log.Warning("低餘額警告: {UserId} 餘額僅剩 {Balance} {Currency}",
                                userId, currentBalance, balanceInfo.Currency);
                        }
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 步驟 4: 遊戲開始 (GameService.GameSessionManager)
                var gameId = $"GAME_{random.Next(1, 99)}";
                var tableId = $"TABLE_{random.Next(1, 20)}";
                string gameType;
                using (var stepActivity = activitySource.StartActivity("4-GameStart", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_GAME);
                    stepActivity?.SetTag("operation", "GameSessionManager");

                    var gameDetails = new
                    {
                        GameType = new[] { "Baccarat", "BlackJack", "Roulette", "DragonTiger" }[random.Next(4)],
                        GameId = gameId,
                        TableId = tableId,
                        Dealer = $"DEALER_{random.Next(1, 50)}",
                        MinBet = 10,
                        MaxBet = 1000
                    };
                    gameType = gameDetails.GameType;

                    stepActivity?.SetTag("game.id", gameId);
                    stepActivity?.SetTag("game.type", gameType);
                    stepActivity?.SetTag("game.table_id", tableId);

                    using (LogContext.PushProperty("ServiceName", SERVICE_GAME))
                    using (LogContext.PushProperty("SourceContext", "GameSessionManager"))
                    using (LogContext.PushProperty("WorkflowStep", "4-GameStart"))
                    using (LogContext.PushProperty("EventType", "GameStarted"))
                    using (LogContext.PushProperty("GameId", gameId))
                    using (LogContext.PushProperty("TableId", tableId))
                    using (LogContext.PushProperty("GameDetails", gameDetails, destructureObjects: true))
                    {
                        Log.Information("遊戲開始: {GameType} at {TableId}, Dealer: {Dealer}",
                            gameDetails.GameType, tableId, gameDetails.Dealer);

                        // 遊戲連線問題警告 (15% 機率)
                        if (random.Next(0, 100) < 15)
                        {
                            var latency = random.Next(500, 2000);
                            stepActivity?.SetTag("connection.latency_ms", latency);
                            var connectionIssue = new
                            {
                                Issue = "HighLatency",
                                Latency = latency,
                                TableId = tableId,
                                Timestamp = DateTime.UtcNow
                            };
                            using (LogContext.PushProperty("ConnectionIssue", connectionIssue, destructureObjects: true))
                            {
                                Log.Warning("遊戲連線延遲: {TableId} 延遲 {Latency}ms",
                                    tableId, latency);
                            }
                        }
                    }

                    await Task.Delay(200, cancellationToken);
                }

                // 步驟 5: 下注 (GameService.BettingHandler)
                var betId = Guid.NewGuid().ToString();
                var betAmount = random.Next(10, 500);
                string betType = "";

                using (var stepActivity = activitySource.StartActivity("5-PlaceBet", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_GAME);
                    stepActivity?.SetTag("bet.id", betId);

                    // 檢查餘額是否足夠 (10% 機率不足) - WalletService 驗證
                    if (random.Next(0, 10) == 0)
                    {
                        betAmount = currentBalance + random.Next(100, 500); // 超過餘額
                        stepActivity?.SetStatus(ActivityStatusCode.Error, "Insufficient balance");
                        stepActivity?.SetTag("error.type", "InsufficientBalance");

                        var insufficientError = new
                        {
                            BetId = betId,
                            RequestedAmount = betAmount,
                            AvailableBalance = currentBalance,
                            Shortage = betAmount - currentBalance,
                            Currency = balanceCurrency
                        };

                        using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                        using (LogContext.PushProperty("SourceContext", "BalanceValidator"))
                        using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
                        using (LogContext.PushProperty("EventType", "BetRejected"))
                        using (LogContext.PushProperty("BetId", betId))
                        using (LogContext.PushProperty("InsufficientError", insufficientError, destructureObjects: true))
                        {
                            Log.Error("下注失敗 - 餘額不足: {UserId} 嘗試下注 {RequestedAmount}，但餘額僅 {AvailableBalance}",
                                userId, betAmount, currentBalance);
                        }

                        // 發送通知 (NotificationService)
                        using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                        using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
                        {
                            Log.Warning("BettingWorkflow 因餘額不足而終止 for {UserId}", userId);
                        }
                        workflowActivity?.SetStatus(ActivityStatusCode.Error, "Insufficient balance");
                        await Task.Delay(2000, cancellationToken);
                        continue;
                    }

                    // 檢查下注是否超過最大限制 (5% 機率)
                    if (betAmount > 1000 && random.Next(0, 20) == 0)
                    {
                        var limitError = new
                        {
                            BetId = betId,
                            RequestedAmount = betAmount,
                            MaxLimit = 1000,
                            ExcessAmount = betAmount - 1000
                        };

                        using (LogContext.PushProperty("ServiceName", SERVICE_GAME))
                        using (LogContext.PushProperty("SourceContext", "BettingLimitValidator"))
                        using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
                        using (LogContext.PushProperty("EventType", "BetLimitExceeded"))
                        using (LogContext.PushProperty("BetId", betId))
                        using (LogContext.PushProperty("LimitError", limitError, destructureObjects: true))
                        {
                            Log.Warning("下注警告 - 超過限額: {UserId} 嘗試下注 {RequestedAmount}，超過最大限額 {MaxLimit}",
                                userId, betAmount, 1000);
                        }

                        betAmount = 1000; // 調整為最大限額
                    }

                    betType = new[] { "Player", "Banker", "Tie", "Red", "Black" }[random.Next(5)];
                    var betDetails = new
                    {
                        BetId = betId,
                        Amount = betAmount,
                        Currency = balanceCurrency,
                        BetType = betType,
                        RemainingBalance = currentBalance - betAmount,
                        Timestamp = DateTime.UtcNow
                    };

                    stepActivity?.SetTag("bet.amount", betAmount);
                    stepActivity?.SetTag("bet.type", betType);
                    stepActivity?.SetTag("bet.currency", balanceCurrency);

                    using (LogContext.PushProperty("ServiceName", SERVICE_GAME))
                    using (LogContext.PushProperty("SourceContext", "BettingHandler"))
                    using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
                    using (LogContext.PushProperty("EventType", "BetPlaced"))
                    using (LogContext.PushProperty("BetId", betId))
                    using (LogContext.PushProperty("BetDetails", betDetails, destructureObjects: true))
                    {
                        Log.Information("下注: {UserId} bet {Amount} {Currency} on {BetType}",
                            userId, betAmount, balanceCurrency, betType);
                    }

                    await Task.Delay(300, cancellationToken);
                }

                // 步驟 6: 遊戲結果 (GameService.ResultHandler)
                string gameResult;
                using (var stepActivity = activitySource.StartActivity("6-GameResult", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_GAME);
                    var gameRound = $"ROUND_{random.Next(10000, 99999)}";
                    var resultDetails = new
                    {
                        Result = new[] { "Player Win", "Banker Win", "Tie", "Red", "Black" }[random.Next(5)],
                        Cards = $"{random.Next(1, 14)}-{random.Next(1, 14)}-{random.Next(1, 14)}",
                        GameRound = gameRound,
                        Timestamp = DateTime.UtcNow
                    };
                    gameResult = resultDetails.Result;

                    stepActivity?.SetTag("game.round", gameRound);
                    stepActivity?.SetTag("game.result", gameResult);

                    using (LogContext.PushProperty("ServiceName", SERVICE_GAME))
                    using (LogContext.PushProperty("SourceContext", "ResultHandler"))
                    using (LogContext.PushProperty("WorkflowStep", "6-GameResult"))
                    using (LogContext.PushProperty("EventType", "GameResult"))
                    using (LogContext.PushProperty("ResultDetails", resultDetails, destructureObjects: true))
                    {
                        Log.Information("遊戲結果: {Result}, Cards: {Cards}, Round: {GameRound}",
                            resultDetails.Result, resultDetails.Cards, gameRound);
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 步驟 7: 注單結算 (WalletService.SettlementProcessor)
                var isWin = random.Next(0, 2) == 1;
                var winAmount = isWin ? betAmount * random.Next(2, 5) : 0;
                var profit = winAmount - betAmount;
                var transactionId = Guid.NewGuid().ToString();

                using (var stepActivity = activitySource.StartActivity("7-Settlement", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_WALLET);
                    stepActivity?.SetTag("transaction.id", transactionId);
                    stepActivity?.SetTag("bet.result", isWin ? "Win" : "Loss");
                    stepActivity?.SetTag("bet.profit", profit);

                    var settlementDetails = new
                    {
                        BetId = betId,
                        TransactionId = transactionId,
                        BetAmount = betAmount,
                        WinAmount = winAmount,
                        Profit = profit,
                        Status = isWin ? "Win" : "Loss",
                        SettledAt = DateTime.UtcNow
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                    using (LogContext.PushProperty("SourceContext", "SettlementProcessor"))
                    using (LogContext.PushProperty("WorkflowStep", "7-Settlement"))
                    using (LogContext.PushProperty("EventType", "BetSettled"))
                    using (LogContext.PushProperty("TransactionId", transactionId))
                    using (LogContext.PushProperty("SettlementDetails", settlementDetails, destructureObjects: true))
                    {
                        Log.Information("注單結算: {Status}, Bet: {BetAmount}, Win: {WinAmount}, Profit: {Profit}",
                            settlementDetails.Status, betAmount, winAmount, profit);

                        // 結算延遲警告 (10% 機率)
                        if (random.Next(0, 10) == 0)
                        {
                            var delayInfo = new
                            {
                                TransactionId = transactionId,
                                DelaySeconds = random.Next(3, 10),
                                Reason = new[] { "DatabaseLatency", "HighLoad", "NetworkIssue" }[random.Next(3)]
                            };
                            using (LogContext.PushProperty("DelayInfo", delayInfo, destructureObjects: true))
                            {
                                Log.Warning("結算延遲: Transaction {TransactionId} 延遲 {DelaySeconds} 秒，原因: {Reason}",
                                    transactionId, delayInfo.DelaySeconds, delayInfo.Reason);
                            }
                        }
                    }

                    await Task.Delay(100, cancellationToken);
                }

                // 步驟 8: 餘額更新 (WalletService.BalanceManager)
                using (var stepActivity = activitySource.StartActivity("8-BalanceUpdate", ActivityKind.Internal))
                {
                    stepActivity?.SetTag("service.name", SERVICE_WALLET);
                    var newBalance = currentBalance + profit;
                    stepActivity?.SetTag("wallet.previous_balance", currentBalance);
                    stepActivity?.SetTag("wallet.new_balance", newBalance);

                    var balanceChange = new
                    {
                        PreviousBalance = currentBalance,
                        NewBalance = newBalance,
                        ChangeAmount = profit,
                        ChangeType = profit >= 0 ? "Credit" : "Debit",
                        TransactionId = transactionId,
                        Timestamp = DateTime.UtcNow
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                    using (LogContext.PushProperty("SourceContext", "BalanceManager"))
                    using (LogContext.PushProperty("WorkflowStep", "8-BalanceUpdate"))
                    using (LogContext.PushProperty("EventType", "BalanceUpdated"))
                    using (LogContext.PushProperty("BalanceChange", balanceChange, destructureObjects: true))
                    {
                        Log.Information("餘額更新: {PreviousBalance} -> {NewBalance} ({ChangeType}: {ChangeAmount})",
                            currentBalance, newBalance, balanceChange.ChangeType, Math.Abs(profit));

                        // 餘額更新錯誤 (5% 機率)
                        if (random.Next(0, 20) == 0)
                        {
                            stepActivity?.SetStatus(ActivityStatusCode.Error, "Database write failed");
                            var updateError = new
                            {
                                TransactionId = transactionId,
                                ErrorCode = "BALANCE_UPDATE_FAILED",
                                ErrorMessage = "資料庫寫入失敗，將重試",
                                RetryAttempt = 1,
                                MaxRetries = 3
                            };
                            using (LogContext.PushProperty("UpdateError", updateError, destructureObjects: true))
                            {
                                Log.Error("餘額更新失敗: Transaction {TransactionId}，錯誤: {ErrorMessage}",
                                    transactionId, updateError.ErrorMessage);
                            }
                        }
                    }
                }

                // 完成通知 (NotificationService)
                using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                using (LogContext.PushProperty("SourceContext", "WorkflowNotifier"))
                {
                    Log.Information("BettingWorkflow completed for {UserId}", userId);
                }
            }

            await Task.Delay(2000, cancellationToken); // 每 2 秒執行一次
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in BettingWorkflow");
            await Task.Delay(2000, cancellationToken);
        }
    }
}

// Workflow 2: 存款流程 (DepositWorkflow) - 4 步驟
// 模擬跨服務調用: WalletService -> PaymentService -> WalletService
async Task RunDepositWorkflow(CancellationToken cancellationToken)
{
    var random = new Random();
    var workflowName = "DepositWorkflow";

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(500, cancellationToken); // 稍微延遲開始時間

            var userId = $"USER_{random.Next(100, 999)}";
            var sessionId = Guid.NewGuid().ToString();

            // 創建 Workflow Span
            using var workflowActivity = activitySource.StartActivity(workflowName, ActivityKind.Internal);
            workflowActivity?.SetTag("user.id", userId);
            workflowActivity?.SetTag("session.id", sessionId);
            workflowActivity?.SetTag("workflow.name", workflowName);

            using (LogContext.PushProperty("UserId", userId))
            using (LogContext.PushProperty("SessionId", sessionId))
            using (LogContext.PushProperty("WorkflowName", workflowName))
            {
                // 步驟 1: 發起存款 (WalletService.DepositRequestHandler)
                var depositAmount = random.Next(100, 5000);
                var depositRequest = new
                {
                    Amount = depositAmount,
                    Currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)],
                    PaymentMethod = new[] { "CreditCard", "BankTransfer", "EWallet", "Crypto" }[random.Next(4)],
                    RequestedAt = DateTime.UtcNow
                };

                using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                using (LogContext.PushProperty("SourceContext", "DepositRequestHandler"))
                using (LogContext.PushProperty("WorkflowStep", "1-InitiateDeposit"))
                using (LogContext.PushProperty("EventType", "DepositInitiated"))
                using (LogContext.PushProperty("DepositRequest", depositRequest, destructureObjects: true))
                {
                    Log.Information("發起存款: {UserId} requests {Amount} {Currency} via {PaymentMethod}",
                        userId, depositAmount, depositRequest.Currency, depositRequest.PaymentMethod);

                    // 金額超過限制警告 (15% 機率) - RiskService 檢查
                    if (depositAmount > 3000 && random.Next(0, 100) < 15)
                    {
                        using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                        using (LogContext.PushProperty("SourceContext", "TransactionLimitChecker"))
                        {
                            var limitWarning = new
                            {
                                Amount = depositAmount,
                                DailyLimit = 10000,
                                SingleTransactionLimit = 5000,
                                RequiresAdditionalVerification = depositAmount > 5000
                            };
                            using (LogContext.PushProperty("LimitWarning", limitWarning, destructureObjects: true))
                            {
                                Log.Warning("存款金額警告: {UserId} 存款 {Amount} 接近限額，需要額外驗證: {RequiresAdditionalVerification}",
                                    userId, depositAmount, limitWarning.RequiresAdditionalVerification);
                            }
                        }
                    }
                }

                await Task.Delay(100, cancellationToken);

                // 步驟 2: 驗證支付方式 (PaymentService.PaymentValidator)
                var isValidationSuccess = random.Next(0, 100) > 5; // 95% 成功率
                var validationDetails = new
                {
                    IsValid = isValidationSuccess,
                    ValidationRules = new[] { "AmountLimit", "PaymentMethodActive", "KYCVerified" },
                    ProcessorId = $"PROC_{random.Next(1, 10)}",
                    ValidatedAt = DateTime.UtcNow,
                    FailureReason = isValidationSuccess ? null : new[] { "KYC_NOT_VERIFIED", "PAYMENT_METHOD_SUSPENDED", "ACCOUNT_RESTRICTED" }[random.Next(3)]
                };

                using (LogContext.PushProperty("ServiceName", SERVICE_PAYMENT))
                using (LogContext.PushProperty("SourceContext", "PaymentValidator"))
                using (LogContext.PushProperty("WorkflowStep", "2-ValidatePayment"))
                using (LogContext.PushProperty("EventType", isValidationSuccess ? "PaymentValidated" : "PaymentValidationFailed"))
                using (LogContext.PushProperty("ValidationDetails", validationDetails, destructureObjects: true))
                {
                    if (isValidationSuccess)
                    {
                        Log.Information("驗證支付方式: {PaymentMethod} is valid, Processor: {ProcessorId}",
                            depositRequest.PaymentMethod, validationDetails.ProcessorId);
                    }
                    else
                    {
                        Log.Error("驗證失敗: {UserId} 的支付方式 {PaymentMethod} 驗證失敗，原因: {FailureReason}",
                            userId, depositRequest.PaymentMethod, validationDetails.FailureReason);
                    }
                }

                if (!isValidationSuccess)
                {
                    // 發送通知 (NotificationService)
                    using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                    using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
                    {
                        Log.Warning("DepositWorkflow 因驗證失敗而終止 for {UserId}", userId);
                    }
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }

                await Task.Delay(200, cancellationToken);

                // 步驟 3: 處理支付 (PaymentService.PaymentProcessor)
                var transactionId = Guid.NewGuid().ToString();
                var isSuccess = random.Next(0, 10) > 0; // 90% 成功率
                var fee = depositAmount * 0.02m; // 2% 手續費

                // 支付處理器連線問題 (8% 機率)
                if (random.Next(0, 100) < 8)
                {
                    var connectionError = new
                    {
                        ProcessorId = validationDetails.ProcessorId,
                        ErrorType = "ConnectionTimeout",
                        RetryCount = random.Next(1, 4),
                        Timestamp = DateTime.UtcNow
                    };
                    using (LogContext.PushProperty("ServiceName", SERVICE_PAYMENT))
                    using (LogContext.PushProperty("SourceContext", "PaymentGatewayClient"))
                    using (LogContext.PushProperty("WorkflowStep", "3-ProcessPayment"))
                    using (LogContext.PushProperty("EventType", "PaymentProcessorConnectionError"))
                    using (LogContext.PushProperty("TransactionId", transactionId))
                    using (LogContext.PushProperty("ConnectionError", connectionError, destructureObjects: true))
                    {
                        Log.Warning("支付處理器連線問題: Processor {ProcessorId} 連線超時，重試次數: {RetryCount}",
                            validationDetails.ProcessorId, connectionError.RetryCount);
                    }
                }

                var paymentDetails = new
                {
                    TransactionId = transactionId,
                    Status = isSuccess ? "Success" : "Failed",
                    Amount = depositAmount,
                    Fee = fee,
                    NetAmount = depositAmount - (decimal)fee,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorCode = isSuccess ? null : new[] { "INSUFFICIENT_FUNDS", "CARD_DECLINED", "BANK_REJECTION", "FRAUD_DETECTED" }[random.Next(4)]
                };

                using (LogContext.PushProperty("ServiceName", SERVICE_PAYMENT))
                using (LogContext.PushProperty("SourceContext", "PaymentProcessor"))
                using (LogContext.PushProperty("WorkflowStep", "3-ProcessPayment"))
                using (LogContext.PushProperty("EventType", isSuccess ? "PaymentProcessed" : "PaymentFailed"))
                using (LogContext.PushProperty("TransactionId", transactionId))
                using (LogContext.PushProperty("PaymentDetails", paymentDetails, destructureObjects: true))
                {
                    if (isSuccess)
                    {
                        Log.Information("處理支付成功: Transaction {TransactionId}, Amount: {Amount}, Fee: {Fee}",
                            transactionId, depositAmount, fee);
                    }
                    else
                    {
                        Log.Error("處理支付失敗: Transaction {TransactionId}, Error: {ErrorCode}",
                            transactionId, paymentDetails.ErrorCode);
                    }
                }

                await Task.Delay(100, cancellationToken);

                // 步驟 4: 餘額入帳 (WalletService.BalanceManager) - 僅在成功時
                if (isSuccess)
                {
                    var newBalance = random.Next(1000, 20000);
                    var creditDetails = new
                    {
                        Amount = paymentDetails.NetAmount,
                        TransactionId = transactionId,
                        NewBalance = newBalance,
                        CreditedAt = DateTime.UtcNow
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                    using (LogContext.PushProperty("SourceContext", "BalanceManager"))
                    using (LogContext.PushProperty("WorkflowStep", "4-CreditBalance"))
                    using (LogContext.PushProperty("EventType", "BalanceCredited"))
                    using (LogContext.PushProperty("CreditDetails", creditDetails, destructureObjects: true))
                    {
                        Log.Information("餘額入帳: {Amount} credited, New Balance: {NewBalance}",
                            creditDetails.Amount, newBalance);
                    }
                }

                // 完成通知 (NotificationService)
                using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                using (LogContext.PushProperty("SourceContext", "WorkflowNotifier"))
                {
                    Log.Information("DepositWorkflow completed for {UserId}", userId);
                }
            }

            await Task.Delay(2000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in DepositWorkflow");
            await Task.Delay(2000, cancellationToken);
        }
    }
}

// Workflow 3: 提款流程 (WithdrawalWorkflow) - 3 步驟
// 模擬跨服務調用: WalletService -> RiskService -> PaymentService
async Task RunWithdrawalWorkflow(CancellationToken cancellationToken)
{
    var random = new Random();
    var workflowName = "WithdrawalWorkflow";

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(1000, cancellationToken); // 稍微延遲開始時間

            var userId = $"USER_{random.Next(100, 999)}";
            var sessionId = Guid.NewGuid().ToString();

            // 創建 Workflow Span
            using var workflowActivity = activitySource.StartActivity(workflowName, ActivityKind.Internal);
            workflowActivity?.SetTag("user.id", userId);
            workflowActivity?.SetTag("session.id", sessionId);
            workflowActivity?.SetTag("workflow.name", workflowName);

            using (LogContext.PushProperty("UserId", userId))
            using (LogContext.PushProperty("SessionId", sessionId))
            using (LogContext.PushProperty("WorkflowName", workflowName))
            {
                // 步驟 1: 提款請求 (WalletService.WithdrawalRequestHandler)
                var withdrawalAmount = random.Next(100, 3000);
                var userBalance = random.Next(0, 5000);

                // 餘額不足錯誤 (12% 機率) - WalletService.BalanceValidator
                if (userBalance < withdrawalAmount && random.Next(0, 100) < 12)
                {
                    var insufficientBalanceError = new
                    {
                        RequestedAmount = withdrawalAmount,
                        AvailableBalance = userBalance,
                        Shortage = withdrawalAmount - userBalance,
                        Currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)]
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                    using (LogContext.PushProperty("SourceContext", "BalanceValidator"))
                    using (LogContext.PushProperty("WorkflowStep", "1-RequestWithdrawal"))
                    using (LogContext.PushProperty("EventType", "WithdrawalRejectedInsufficientBalance"))
                    using (LogContext.PushProperty("InsufficientBalanceError", insufficientBalanceError, destructureObjects: true))
                    {
                        Log.Error("提款失敗 - 餘額不足: {UserId} 請求提款 {RequestedAmount}，但餘額僅 {AvailableBalance}",
                            userId, withdrawalAmount, userBalance);
                    }

                    // 發送通知 (NotificationService)
                    using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                    using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
                    {
                        Log.Warning("WithdrawalWorkflow 因餘額不足而終止 for {UserId}", userId);
                    }
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }

                // 帳戶凍結錯誤 (5% 機率) - RiskService.AccountStatusChecker
                if (random.Next(0, 100) < 5)
                {
                    var accountFrozenError = new
                    {
                        Reason = new[] { "SUSPECTED_FRAUD", "PENDING_INVESTIGATION", "COMPLIANCE_HOLD", "DUPLICATE_ACCOUNT" }[random.Next(4)],
                        FrozenSince = DateTime.UtcNow.AddDays(-random.Next(1, 30)),
                        ContactSupport = true
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                    using (LogContext.PushProperty("SourceContext", "AccountStatusChecker"))
                    using (LogContext.PushProperty("WorkflowStep", "1-RequestWithdrawal"))
                    using (LogContext.PushProperty("EventType", "WithdrawalRejectedAccountFrozen"))
                    using (LogContext.PushProperty("AccountFrozenError", accountFrozenError, destructureObjects: true))
                    {
                        Log.Error("提款失敗 - 帳戶凍結: {UserId} 帳戶已凍結，原因: {Reason}",
                            userId, accountFrozenError.Reason);
                    }

                    // 發送通知 (NotificationService)
                    using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                    using (LogContext.PushProperty("SourceContext", "AlertDispatcher"))
                    {
                        Log.Warning("WithdrawalWorkflow 因帳戶凍結而終止 for {UserId}", userId);
                    }
                    await Task.Delay(2000, cancellationToken);
                    continue;
                }

                var withdrawalRequest = new
                {
                    Amount = withdrawalAmount,
                    Currency = new[] { "USD", "TWD", "HKD", "SGD" }[random.Next(4)],
                    WithdrawalMethod = new[] { "BankTransfer", "EWallet", "Crypto", "Check" }[random.Next(4)],
                    AccountInfo = $"****{random.Next(1000, 9999)}",
                    RequestedAt = DateTime.UtcNow
                };

                using (LogContext.PushProperty("ServiceName", SERVICE_WALLET))
                using (LogContext.PushProperty("SourceContext", "WithdrawalRequestHandler"))
                using (LogContext.PushProperty("WorkflowStep", "1-RequestWithdrawal"))
                using (LogContext.PushProperty("EventType", "WithdrawalRequested"))
                using (LogContext.PushProperty("WithdrawalRequest", withdrawalRequest, destructureObjects: true))
                {
                    Log.Information("提款請求: {UserId} requests {Amount} {Currency} via {WithdrawalMethod}",
                        userId, withdrawalAmount, withdrawalRequest.Currency, withdrawalRequest.WithdrawalMethod);
                }

                // KYC 未完成警告 (10% 機率) - PlayerService.KYCValidator
                if (random.Next(0, 10) == 0)
                {
                    var kycWarning = new
                    {
                        KYCStatus = "Incomplete",
                        MissingDocuments = new[] { "ID Verification", "Address Proof" },
                        RequiredForAmount = withdrawalAmount > 1000
                    };
                    using (LogContext.PushProperty("ServiceName", SERVICE_PLAYER))
                    using (LogContext.PushProperty("SourceContext", "KYCValidator"))
                    using (LogContext.PushProperty("KYCWarning", kycWarning, destructureObjects: true))
                    {
                        Log.Warning("KYC 警告: {UserId} KYC 未完成，缺少文件，大額提款需要完成 KYC",
                            userId);
                    }
                }

                // 超過每日提款限制警告 (8% 機率) - RiskService.TransactionLimitChecker
                if (withdrawalAmount > 2000 && random.Next(0, 100) < 8)
                {
                    var dailyLimitWarning = new
                    {
                        RequestedAmount = withdrawalAmount,
                        DailyLimit = 5000,
                        TodayWithdrawn = random.Next(1000, 3000),
                        RemainingLimit = 5000 - random.Next(1000, 3000)
                    };
                    using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                    using (LogContext.PushProperty("SourceContext", "TransactionLimitChecker"))
                    using (LogContext.PushProperty("DailyLimitWarning", dailyLimitWarning, destructureObjects: true))
                    {
                        Log.Warning("每日提款限額警告: {UserId} 請求 {RequestedAmount}，今日已提款 {TodayWithdrawn}，剩餘額度 {RemainingLimit}",
                            userId, withdrawalAmount, dailyLimitWarning.TodayWithdrawn, dailyLimitWarning.RemainingLimit);
                    }
                }

                await Task.Delay(150, cancellationToken);

                // 步驟 2: 風險評估 (RiskService.RiskAssessmentEngine)
                var riskScore = random.Next(0, 100);
                var riskLevel = riskScore < 30 ? "Low" : riskScore < 70 ? "Medium" : "High";
                var riskPassed = riskScore < 70;

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

                using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                using (LogContext.PushProperty("SourceContext", "RiskAssessmentEngine"))
                using (LogContext.PushProperty("WorkflowStep", "2-RiskAssessment"))
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

                // 異常交易模式警告 (10% 機率) - RiskService.PatternDetector
                if (random.Next(0, 10) == 0)
                {
                    var patternWarning = new
                    {
                        Pattern = new[] { "FrequentWithdrawals", "LargeAmountAfterDeposit", "UnusualTiming", "NewDevice" }[random.Next(4)],
                        Confidence = random.Next(60, 95),
                        RequiresReview = true
                    };
                    using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                    using (LogContext.PushProperty("SourceContext", "PatternDetector"))
                    using (LogContext.PushProperty("PatternWarning", patternWarning, destructureObjects: true))
                    {
                        Log.Warning("異常交易模式: {UserId} 偵測到 {Pattern}，信心度: {Confidence}%",
                            userId, patternWarning.Pattern, patternWarning.Confidence);
                    }
                }

                await Task.Delay(200, cancellationToken);

                // 步驟 3: 核准或標記
                var transactionId = Guid.NewGuid().ToString();

                if (riskPassed)
                {
                    // 核准 (PaymentService.WithdrawalApprover)
                    var approvalDetails = new
                    {
                        TransactionId = transactionId,
                        ApprovedBy = "AutomatedSystem",
                        ApprovedAt = DateTime.UtcNow,
                        EstimatedCompletionTime = DateTime.UtcNow.AddHours(24)
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_PAYMENT))
                    using (LogContext.PushProperty("SourceContext", "WithdrawalApprover"))
                    using (LogContext.PushProperty("WorkflowStep", "3-Approval"))
                    using (LogContext.PushProperty("EventType", "WithdrawalApproved"))
                    using (LogContext.PushProperty("TransactionId", transactionId))
                    using (LogContext.PushProperty("ApprovalDetails", approvalDetails, destructureObjects: true))
                    {
                        Log.Information("提款核准: Transaction {TransactionId}, Estimated completion in 24h",
                            transactionId);
                    }
                }
                else
                {
                    // 標記需要人工審核 (RiskService.ManualReviewQueue)
                    var flagDetails = new
                    {
                        TransactionId = transactionId,
                        Reason = new[] { "HighRiskScore", "UnusualPattern", "LargeAmount", "NewAccount" }[random.Next(4)],
                        RequiresManualReview = true,
                        FlaggedAt = DateTime.UtcNow,
                        ReviewerAssigned = $"REVIEWER_{random.Next(1, 10)}"
                    };

                    using (LogContext.PushProperty("ServiceName", SERVICE_RISK))
                    using (LogContext.PushProperty("SourceContext", "ManualReviewQueue"))
                    using (LogContext.PushProperty("WorkflowStep", "3-FlaggedReview"))
                    using (LogContext.PushProperty("EventType", "WithdrawalFlagged"))
                    using (LogContext.PushProperty("TransactionId", transactionId))
                    using (LogContext.PushProperty("FlagDetails", flagDetails, destructureObjects: true))
                    {
                        Log.Warning("提款標記審核: Transaction {TransactionId}, Reason: {Reason}, Reviewer: {ReviewerAssigned}",
                            transactionId, flagDetails.Reason, flagDetails.ReviewerAssigned);
                    }
                }

                // 完成通知 (NotificationService)
                using (LogContext.PushProperty("ServiceName", SERVICE_NOTIFICATION))
                using (LogContext.PushProperty("SourceContext", "WorkflowNotifier"))
                {
                    Log.Information("WithdrawalWorkflow completed for {UserId}", userId);
                }
            }

            await Task.Delay(2000, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in WithdrawalWorkflow");
            await Task.Delay(2000, cancellationToken);
        }
    }
}

// ActivityEnricher: 自動將 Activity 的 TraceId/SpanId 加入到 log properties
class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId", activity.ParentSpanId.ToString()));
        }
    }
}
