namespace SeqDemo.Shared.Events;

public record WorkflowCompletedEvent(
    string UserId,
    string WorkflowName,
    string SessionId,
    DateTime CompletedAt
);
