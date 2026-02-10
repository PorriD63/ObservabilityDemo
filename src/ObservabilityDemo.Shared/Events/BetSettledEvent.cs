namespace ObservabilityDemo.Shared.Events;

public record BetSettledEvent(
    string UserId,
    string BetId,
    string TransactionId,
    int BetAmount,
    int WinAmount,
    int Profit,
    string Status,
    DateTime SettledAt
);
