namespace NAP.Core;

/// <summary>A non-authoritative sibling temp, identified solely by its canonical simple filename.</summary>
public sealed record JobRecoveryTempArtifact
{
    public JobRecoveryTempArtifact(JobId jobId, string fileName, bool hasFinalJournal)
    {
        ArgumentNullException.ThrowIfNull(jobId);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length != 73 ||
            !fileName.StartsWith(jobId.Value + ".", StringComparison.Ordinal) ||
            !fileName.EndsWith(".tmp", StringComparison.Ordinal) ||
            fileName.Skip(37).Take(32).Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new ArgumentException("The temporary filename must be the matching Job ID, a dot, 32 lowercase ASCII hexadecimal characters and '.tmp'.", nameof(fileName));
        JobId = jobId;
        FileName = fileName;
        HasFinalJournal = hasFinalJournal;
    }

    public JobId JobId { get; }
    public string FileName { get; }
    public bool HasFinalJournal { get; }
}
