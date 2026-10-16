namespace NAP.Core;

/// <summary>Optional internal boundary for durable automation. The manual API retains its existing contract.
/// Callbacks finish their own short ledger operations before the engine performs an external effect.</summary>
internal sealed record WorkflowExecutionControl(Action<string> Before, Action<string> After,
    Action<ArchiveMasterPlan> VerifyArchiveOwnership);
