namespace SeqDemo.Shared.Events;

public record WithdrawalApprovedEvent(
    string UserId,
    string TransactionId,
    int Amount,
    string Currency,
    string ApprovedBy,
    DateTime ApprovedAt
);
