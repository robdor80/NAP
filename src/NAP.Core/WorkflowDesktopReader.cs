namespace NAP.Core;

/// <summary>Bounded display-only projections; never changes or repairs a workflow.</summary>
public sealed class WorkflowDesktopReader(UniverseContext context, AutomationQueueStore queue)
{
    public WorkflowRow Row(QueueItem item)
    {
        var name = Path.GetFileNameWithoutExtension(item.SourcePath);
        if (item.ActiveAttempt is not null)
        {
            try { name = new InboxAdmissionService(context, queue).GetReceipt(item.Id).AssetId ?? name; }
            catch (AutomationException e) when (e.Code == AutomationError.Missing) { }
        }
        return new(item.Id, item.UniverseId, name, item.State, item.Incident, item.CreatedUtc, item.UpdatedUtc, item.BatchId, item.Revision);
    }
    public WorkflowPage Browse(WorkflowPageRequest request)
    {
        var page = queue.Browse(request);
        return new(context.Id, request.Offset, page.Total, page.Items.Select(Row).ToArray());
    }
    public WorkflowDetail Detail(WorkflowDetailRequest request, ReviewCapability capabilities)
    {
        var item = queue.Get(request.ItemId); var service = new DurablePreparationService(context, queue);
        var events = queue.BrowseEvents(item.Id, request.EventOffset);
        // A missing/corrupt image does not hide the durable incident or fabricate a preview.
        byte[]? image = null; ReviewPreview? preview = null; IReadOnlyList<ReviewChoice> choices = [];
        try
        {
            image = service.ReadPreservedPreview(item.Id);
            if (capabilities.CanPropose) choices = service.ReadReviewChoices(item.Id);
            if (service.CanResolveException(item.Id)) preview = service.ReadReviewPreview(item.Id);
        }
        catch (Exception e) when (e is AutomationException or IOException or UnauthorizedAccessException or ImageNormalizationException or PackagePreparationStoppedException)
        { capabilities = new(false, false, false); image = null; preview = null; choices = []; }
        return new(context.Id, Row(item), events.Take(20).ToArray(), events.Count > 20, choices, preview, preview is null ? image : null,
            capabilities.CanPropose && choices.Count > 0, capabilities.CanApprove && preview?.State == ExceptionalDecisionState.Proposed,
            capabilities.CanContinue && preview?.State == ExceptionalDecisionState.Approved);
    }
}
