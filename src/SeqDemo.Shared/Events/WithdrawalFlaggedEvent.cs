namespace SeqDemo.Shared.Events;

public record WithdrawalFlaggedEvent(
    string UserId,
    string TransactionId,
    int Amount,
    string Reason,
    string ReviewerAssigned,
    DateTime FlaggedAt
);
