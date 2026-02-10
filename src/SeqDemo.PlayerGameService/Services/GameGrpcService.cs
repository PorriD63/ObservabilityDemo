using System.Diagnostics;
using Grpc.Core;
using Serilog;
using Serilog.Context;
using SeqDemo.Shared.Constants;
using SeqDemo.Shared.Protos;

namespace SeqDemo.PlayerGameService.Services;

/// <summary>
/// GameService gRPC 實作 — 遊戲開始、下注、遊戲結果。
/// 對應原始 Program.cs 中 SERVICE_GAME 的遊戲業務邏輯。
/// 下注時透過 gRPC client 呼叫 FinanceService 查詢餘額。
/// </summary>
public class GameGrpcService : GameService.GameServiceBase
{
    public record Dependencies(ActivitySource ActivitySource, Grpc.Net.Client.GrpcChannel FinanceChannel);

    private readonly ActivitySource _activitySource;
    private readonly WalletService.WalletServiceClient _walletClient;

    public GameGrpcService(Dependencies deps)
    {
        _activitySource = deps.ActivitySource;
        _walletClient = new WalletService.WalletServiceClient(deps.FinanceChannel);
    }

    /// <summary>
    /// 遊戲開始 (對應 Program.cs L244-303)
    /// </summary>
    public override async Task<StartGameResponse> StartGame(StartGameRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("StartGame", ActivityKind.Server);
        activity?.SetTag("rpc.method", "StartGame");
        activity?.SetTag("rpc.service", "GameService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "GameSessionManager");

        var gameId = $"GAME_{Random.Shared.Next(1, 99)}";
        var tableId = $"TABLE_{Random.Shared.Next(1, 20)}";
        var gameType = new[] { "Baccarat", "BlackJack", "Roulette", "DragonTiger" }[Random.Shared.Next(4)];
        var dealer = $"DEALER_{Random.Shared.Next(1, 50)}";

        activity?.SetTag("game.id", gameId);
        activity?.SetTag("game.type", gameType);
        activity?.SetTag("game.table_id", tableId);

        var gameDetails = new
        {
            GameType = gameType,
            GameId = gameId,
            TableId = tableId,
            Dealer = dealer,
            MinBet = 10,
            MaxBet = 1000
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.GameService))
        using (LogContext.PushProperty("SourceContext", "GameSessionManager"))
        using (LogContext.PushProperty("WorkflowStep", "4-GameStart"))
        using (LogContext.PushProperty("EventType", "GameStarted"))
        using (LogContext.PushProperty("GameId", gameId))
        using (LogContext.PushProperty("TableId", tableId))
        using (LogContext.PushProperty("GameDetails", gameDetails, destructureObjects: true))
        {
            Log.Information("遊戲開始: {GameType} at {TableId}, Dealer: {Dealer}",
                gameType, tableId, dealer);

            // 遊戲連線問題警告 (15% 機率)
            if (Random.Shared.Next(0, 100) < 15)
            {
                var latency = Random.Shared.Next(500, 2000);
                activity?.SetTag("connection.latency_ms", latency);
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

        await Task.Delay(200);

        return new StartGameResponse
        {
            GameId = gameId,
            TableId = tableId,
            GameType = gameType,
            Dealer = dealer,
            MinBet = 10,
            MaxBet = 1000
        };
    }

    /// <summary>
    /// 下注 (對應 Program.cs L305-406)
    /// 包含餘額不足、下注超過限額等錯誤情境。
    /// 透過 gRPC client 呼叫 FinanceService 查詢餘額。
    /// </summary>
    public override async Task<PlaceBetResponse> PlaceBet(PlaceBetRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("PlaceBet", ActivityKind.Server);
        activity?.SetTag("rpc.method", "PlaceBet");
        activity?.SetTag("rpc.service", "GameService");
        activity?.SetTag("rpc.system", "grpc");

        var betId = Guid.NewGuid().ToString();
        var betAmount = request.BetAmount;
        var currentBalance = request.CurrentBalance;
        var balanceCurrency = request.BalanceCurrency;

        activity?.SetTag("bet.id", betId);

        // 檢查餘額是否足夠 (10% 機率不足)
        if (Random.Shared.Next(0, 10) == 0)
        {
            betAmount = currentBalance + Random.Shared.Next(100, 500); // 超過餘額
            activity?.SetStatus(ActivityStatusCode.Error, "Insufficient balance");
            activity?.SetTag("error.type", "InsufficientBalance");

            var insufficientError = new
            {
                BetId = betId,
                RequestedAmount = betAmount,
                AvailableBalance = currentBalance,
                Shortage = betAmount - currentBalance,
                Currency = balanceCurrency
            };

            using (LogContext.PushProperty("ServiceName", ServiceNames.WalletService))
            using (LogContext.PushProperty("SourceContext", "BalanceValidator"))
            using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
            using (LogContext.PushProperty("EventType", "BetRejected"))
            using (LogContext.PushProperty("BetId", betId))
            using (LogContext.PushProperty("InsufficientError", insufficientError, destructureObjects: true))
            {
                Log.Error("下注失敗 - 餘額不足: {UserId} 嘗試下注 {RequestedAmount}，但餘額僅 {AvailableBalance}",
                    request.UserId, betAmount, currentBalance);
            }

            return new PlaceBetResponse
            {
                Success = false,
                BetId = betId,
                ErrorType = "InsufficientBalance",
                ErrorMessage = $"餘額不足: 請求 {betAmount}，可用 {currentBalance}"
            };
        }

        // 檢查下注是否超過最大限制 (5% 機率)
        if (betAmount > 1000 && Random.Shared.Next(0, 20) == 0)
        {
            var limitError = new
            {
                BetId = betId,
                RequestedAmount = betAmount,
                MaxLimit = 1000,
                ExcessAmount = betAmount - 1000
            };

            using (LogContext.PushProperty("ServiceName", ServiceNames.GameService))
            using (LogContext.PushProperty("SourceContext", "BettingLimitValidator"))
            using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
            using (LogContext.PushProperty("EventType", "BetLimitExceeded"))
            using (LogContext.PushProperty("BetId", betId))
            using (LogContext.PushProperty("LimitError", limitError, destructureObjects: true))
            {
                Log.Warning("下注警告 - 超過限額: {UserId} 嘗試下注 {RequestedAmount}，超過最大限額 {MaxLimit}",
                    request.UserId, betAmount, 1000);
            }

            betAmount = 1000; // 調整為最大限額
        }

        var betType = string.IsNullOrEmpty(request.BetType)
            ? new[] { "Player", "Banker", "Tie", "Red", "Black" }[Random.Shared.Next(5)]
            : request.BetType;

        var betDetails = new
        {
            BetId = betId,
            Amount = betAmount,
            Currency = balanceCurrency,
            BetType = betType,
            RemainingBalance = currentBalance - betAmount,
            Timestamp = DateTime.UtcNow
        };

        activity?.SetTag("bet.amount", betAmount);
        activity?.SetTag("bet.type", betType);
        activity?.SetTag("bet.currency", balanceCurrency);

        using (LogContext.PushProperty("ServiceName", ServiceNames.GameService))
        using (LogContext.PushProperty("SourceContext", "BettingHandler"))
        using (LogContext.PushProperty("WorkflowStep", "5-PlaceBet"))
        using (LogContext.PushProperty("EventType", "BetPlaced"))
        using (LogContext.PushProperty("BetId", betId))
        using (LogContext.PushProperty("BetDetails", betDetails, destructureObjects: true))
        {
            Log.Information("下注: {UserId} bet {Amount} {Currency} on {BetType}",
                request.UserId, betAmount, balanceCurrency, betType);
        }

        await Task.Delay(300);

        return new PlaceBetResponse
        {
            Success = true,
            BetId = betId,
            AdjustedAmount = betAmount,
            BetType = betType
        };
    }

    /// <summary>
    /// 遊戲結果 (對應 Program.cs L408-438)
    /// </summary>
    public override async Task<GetGameResultResponse> GetGameResult(GetGameResultRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("GetGameResult", ActivityKind.Server);
        activity?.SetTag("rpc.method", "GetGameResult");
        activity?.SetTag("rpc.service", "GameService");
        activity?.SetTag("rpc.system", "grpc");

        var gameRound = $"ROUND_{Random.Shared.Next(10000, 99999)}";
        var result = new[] { "Player Win", "Banker Win", "Tie", "Red", "Black" }[Random.Shared.Next(5)];
        var cards = $"{Random.Shared.Next(1, 14)}-{Random.Shared.Next(1, 14)}-{Random.Shared.Next(1, 14)}";

        activity?.SetTag("game.round", gameRound);
        activity?.SetTag("game.result", result);

        var resultDetails = new
        {
            Result = result,
            Cards = cards,
            GameRound = gameRound,
            Timestamp = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.GameService))
        using (LogContext.PushProperty("SourceContext", "ResultHandler"))
        using (LogContext.PushProperty("WorkflowStep", "6-GameResult"))
        using (LogContext.PushProperty("EventType", "GameResult"))
        using (LogContext.PushProperty("ResultDetails", resultDetails, destructureObjects: true))
        {
            Log.Information("遊戲結果: {Result}, Cards: {Cards}, Round: {GameRound}",
                result, cards, gameRound);
        }

        await Task.Delay(100);

        return new GetGameResultResponse
        {
            Result = result,
            Cards = cards,
            GameRound = gameRound
        };
    }
}
