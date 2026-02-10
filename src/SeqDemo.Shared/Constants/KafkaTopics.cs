namespace SeqDemo.Shared.Constants;

public static class KafkaTopics
{
    public const string BetSettled = "bet-settled";
    public const string PaymentProcessed = "payment-processed";
    public const string WithdrawalApproved = "withdrawal-approved";
    public const string WithdrawalFlagged = "withdrawal-flagged";
    public const string WorkflowCompleted = "workflow-completed";
    public const string InsufficientBalance = "insufficient-balance";
}
