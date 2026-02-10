namespace SeqDemo.Shared.Events;

public record PaymentProcessedEvent(
    string UserId,
    string TransactionId,
    int Amount,
    double Fee,
    double NetAmount,
    string Status,
    string? ErrorCode,
    DateTime ProcessedAt
);
