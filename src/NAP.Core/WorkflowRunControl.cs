namespace NAP.Core;

/// <summary>Cooperative scheduling restriction; it grants no policy or filesystem capability.
/// Engines consult it only at boundaries whose durable receipts are complete.</summary>
public sealed class WorkflowRunControl(Func<QueueItemId, string, bool> shouldSuspend)
{
    internal void Check(QueueItemId item, string boundary)
    { if (shouldSuspend(item, boundary)) throw new WorkflowSuspendedException(boundary); }
}
internal sealed class WorkflowSuspendedException(string boundary) : Exception(boundary);
