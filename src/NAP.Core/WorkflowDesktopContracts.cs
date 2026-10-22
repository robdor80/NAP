namespace NAP.Core;

public enum WorkflowFilter { All, Pending, Processing, Completed, Review }
public sealed record WorkflowPageRequest(int Offset = 0, int Limit = 20, WorkflowFilter Filter = WorkflowFilter.All,
    string Search = "", QueueBatchId? Batch = null);
public sealed record WorkflowRow(QueueItemId ItemId, UniverseId UniverseId, string Name, QueueState State,
    QueueIncident Incident, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, QueueBatchId? Batch, long Revision);
public sealed record WorkflowPage(UniverseId UniverseId, int Offset, int Total, IReadOnlyList<WorkflowRow> Items);
public sealed record WorkflowDetailRequest(QueueItemId ItemId, int EventOffset = 0);
public sealed record WorkflowDetail(UniverseId UniverseId, WorkflowRow Item, IReadOnlyList<QueueEvent> Events, bool MoreEvents,
    IReadOnlyList<ReviewChoice> Choices, ReviewPreview? Preview, byte[]? OriginalImage, bool CanPropose, bool CanApprove, bool CanContinue);
public sealed record ReviewChoice(string Key, string Label, string Profile, int Width, int Height);
public sealed record ReviewProposalRequest(QueueItemId ItemId, long Revision, string ChoiceKey);
public sealed record ReviewDecisionRequest(QueueItemId ItemId, long Revision, string DecisionId, int Version, Sha256Digest Evidence, bool Confirmed);
public sealed record ReviewPreview(string DecisionId, int Version, Sha256Digest Evidence, ExceptionalDecisionState State,
    int OriginalWidth, int OriginalHeight, int CanvasWidth, int CanvasHeight, int OutputWidth, int OutputHeight,
    int Left, int Top, int Right, int Bottom, string Profile, byte[] OriginalImage, byte[] CandidateImage);
public sealed record ReviewCapability(bool CanPropose, bool CanApprove, bool CanContinue);

public sealed record WorkflowBatch(UniverseId UniverseId, QueueBatchId Id, DateTimeOffset CreatedUtc, int Items);
