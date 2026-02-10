namespace SeqDemo.Shared.Events;

public record InsufficientBalanceEvent(
    string UserId,
    string BetId,
    int RequestedAmount,
    int AvailableBalance,
    string Currency,
    DateTime OccurredAt
);
