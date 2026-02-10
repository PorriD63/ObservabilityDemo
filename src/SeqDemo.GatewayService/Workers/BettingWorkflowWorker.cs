using System.Diagnostics;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Events;
using SeqDemo.Shared.Kafka;
using SeqDemo.Shared.Protos;

namespace SeqDemo.GatewayService.Workers;

/// <summary>
/// BettingWorkflow — 8 步驟下注流程
/// Gateway → PlayerGameService (Login, Authenticate)
/// Gateway → FinanceService (GetBalance)
/// Gateway → PlayerGameService (StartGame, PlaceBet, GetGameResult)
/// Gateway → FinanceService (Settlement, UpdateBalance)
/// </summary>
public class BettingWorkflowWorker : BackgroundService
{
    private readonly ActivitySource _activitySource;
    private readonly PlayerService.PlayerServiceClient _playerClient;
    private readonly GameService.GameServiceClient _gameClient;
    private readonly WalletService.WalletServiceClient _walletClient;
    private readonly KafkaProducer _kafkaProducer;

    public BettingWorkflowWorker(
        ActivitySource activitySource,
        GrpcChannels channels,
        KafkaProducer kafkaProducer)
    {
        _activitySource = activitySource;
        _playerClient = new PlayerService.PlayerServiceClient(channels.PlayerGameChannel);
        _gameClient = new GameService.GameServiceClient(channels.PlayerGameChannel);
        _walletClient = new WalletService.WalletServiceClient(channels.FinanceChannel);
        _kafkaProducer = kafkaProducer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var random = new Random();
        const string workflowName = "BettingWorkflow";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var userId = $"USER_{random.Next(100, 999)}";
                var sessionId = Guid.NewGuid().ToString();

                // Root span — ApiGateway 作為入口
                using var workflowActivity = _activitySource.StartActivity(workflowName, ActivityKind.Server);
                workflowActivity?.SetTag("user.id", userId);
                workflowActivity?.SetTag("session.id", sessionId);
                workflowActivity?.SetTag("workflow.name", workflowName);
                workflowActivity?.SetTag("http.method", "POST");
                workflowActivity?.SetTag("http.url", "/api/betting/workflow");

                using (LogContext.PushProperty("UserId", userId))
                using (LogContext.PushProperty("SessionId", sessionId))
                using (LogContext.PushProperty("WorkflowName", workflowName))
                {
                    // ═══ 步驟 1: 玩家登入 ═══
                    // gRPC → PlayerGameService (PlayerService/Login)
                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "1-Login"))
                    {
                        Log.Debug("Calling PlayerService/Login for {UserId}", userId);
                    }

                    var loginResponse = await _playerClient.LoginAsync(
                        new LoginRequest { UserId = userId, SessionId = sessionId },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "1-Login"))
                    {
                        Log.Information("步驟1完成 - 玩家登入: {UserId} from {Location} using {Device}",
                            userId, loginResponse.Location, loginResponse.Device);
                    }

                    // ═══ 步驟 2: 玩家驗證 ═══
                    // gRPC → PlayerGameService (PlayerService/Authenticate)
                    var authResponse = await _playerClient.AuthenticateAsync(
                        new AuthenticateRequest { UserId = userId, SessionId = sessionId },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "2-Authentication"))
                    {
                        Log.Information("步驟2完成 - 玩家驗證: {UserId} with {AuthMethod}, Role: {Role}",
                            userId, authResponse.AuthMethod, authResponse.Role);
                    }

                    // ═══ 步驟 3: 查詢餘額 ═══
                    // gRPC → FinanceService (WalletService/GetBalance)
                    var balanceResponse = await _walletClient.GetBalanceAsync(
                        new GetBalanceRequest { UserId = userId },
                        cancellationToken: stoppingToken);

                    var currentBalance = balanceResponse.Balance;
                    var balanceCurrency = balanceResponse.Currency;

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "3-BalanceCheck"))
                    {
                        Log.Information("步驟3完成 - 查詢餘額: {UserId} Balance: {Balance} {Currency}",
                            userId, currentBalance, balanceCurrency);
                    }

                    // ═══ 步驟 4: 遊戲開始 ═══
                    // gRPC → PlayerGameService (GameService/StartGame)
                    var startGameResponse = await _gameClient.StartGameAsync(
                        new StartGameRequest { UserId = userId },
                        cancellationToken: stoppingToken);

                    var gameId = startGameResponse.GameId;
                    var tableId = startGameResponse.TableId;
                    var gameType = startGameResponse.GameType;

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "4-GameStart"))
                    using (LogContext.PushProperty("GameId", gameId))
                    {
                        Log.Information("步驟4完成 - 遊戲開始: {GameType} at {TableId}",
                            gameType, tableId);
                    }

                    // ═══ 步驟 5: 下注 ═══
                    // gRPC → PlayerGameService (GameService/PlaceBet)
                    var betAmount = random.Next(10, 500);
                    var betType = new[] { "Player", "Banker", "Tie", "Red", "Black" }[random.Next(5)];

                    var placeBetResponse = await _gameClient.PlaceBetAsync(
                        new PlaceBetRequest
                        {
                            UserId = userId,
                            GameId = gameId,
                            BetAmount = betAmount,
                            BetType = betType,
                            CurrentBalance = currentBalance,
                            BalanceCurrency = balanceCurrency
                        },
                        cancellationToken: stoppingToken);

                    if (!placeBetResponse.Success)
                    {
                        // 餘額不足或下注被拒
                        using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                        using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
                        {
                            Log.Warning("BettingWorkflow 終止: {ErrorType} - {ErrorMessage}",
                                placeBetResponse.ErrorType, placeBetResponse.ErrorMessage);
                        }

                        workflowActivity?.SetStatus(ActivityStatusCode.Error, placeBetResponse.ErrorType);

                        // Kafka publish insufficient-balance event
                        if (placeBetResponse.ErrorType == "InsufficientBalance")
                        {
                            await _kafkaProducer.ProduceAsync(
                                KafkaTopics.InsufficientBalance,
                                userId,
                                new InsufficientBalanceEvent(
                                    userId,
                                    placeBetResponse.BetId,
                                    betAmount,
                                    currentBalance,
                                    balanceCurrency,
                                    DateTime.UtcNow),
                                stoppingToken);
                        }

                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    var actualBetAmount = placeBetResponse.AdjustedAmount > 0
                        ? placeBetResponse.AdjustedAmount
                        : betAmount;

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
                    using (LogContext.PushProperty("BetId", placeBetResponse.BetId))
                    {
                        Log.Information("步驟5完成 - 下注: {UserId} bet {Amount} {Currency} on {BetType}",
                            userId, actualBetAmount, balanceCurrency, placeBetResponse.BetType);
                    }

                    // ═══ 步驟 6: 遊戲結果 ═══
                    // gRPC → PlayerGameService (GameService/GetGameResult)
                    var gameResultResponse = await _gameClient.GetGameResultAsync(
                        new GetGameResultRequest { GameId = gameId },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "6-GameResult"))
                    {
                        Log.Information("步驟6完成 - 遊戲結果: {Result}, Round: {GameRound}",
                            gameResultResponse.Result, gameResultResponse.GameRound);
                    }

                    // ═══ 步驟 7: 注單結算 ═══
                    // gRPC → FinanceService (WalletService/Settlement)
                    var settlementResponse = await _walletClient.SettlementAsync(
                        new SettlementRequest
                        {
                            UserId = userId,
                            BetId = placeBetResponse.BetId,
                            BetAmount = actualBetAmount,
                            BetType = placeBetResponse.BetType,
                            GameResult = gameResultResponse.Result
                        },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "7-Settlement"))
                    using (LogContext.PushProperty("TransactionId", settlementResponse.TransactionId))
                    {
                        Log.Information("步驟7完成 - 注單結算: {Status}, Profit: {Profit}",
                            settlementResponse.IsWin ? "Win" : "Loss", settlementResponse.Profit);
                    }

                    // ═══ 步驟 8: 餘額更新 ═══
                    // gRPC → FinanceService (WalletService/UpdateBalance)
                    var updateResponse = await _walletClient.UpdateBalanceAsync(
                        new UpdateBalanceRequest
                        {
                            UserId = userId,
                            TransactionId = settlementResponse.TransactionId,
                            Profit = settlementResponse.Profit,
                            CurrentBalance = currentBalance
                        },
                        cancellationToken: stoppingToken);

                    using (LogContext.PushProperty("ServiceName", ServiceNames.ApiGateway))
                    using (LogContext.PushProperty("WorkflowStep", "8-BalanceUpdate"))
                    {
                        if (updateResponse.Success)
                        {
                            Log.Information("步驟8完成 - 餘額更新: {PreviousBalance} -> {NewBalance}",
                                updateResponse.PreviousBalance, updateResponse.NewBalance);
                        }
                        else
                        {
                            Log.Warning("步驟8警告 - 餘額更新異常: {ErrorMessage}", updateResponse.ErrorMessage);
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
                        Log.Information("BettingWorkflow completed for {UserId}", userId);
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
                Log.Error(ex, "Error in BettingWorkflow");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
