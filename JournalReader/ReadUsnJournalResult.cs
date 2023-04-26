namespace JournalReader;

internal sealed class ReadUsnJournalResult
{
    /// <summary>
    /// Status indication of the read attempt.
    /// </summary>
    public readonly ReadUsnJournalStatus Status;

    /// <summary>
    /// If the read <see cref="Succeeded"/>, specifies the next USN that will be recorded in the journal
    /// (a continuation cursor for futher reads).
    /// </summary>
    public readonly Usn NextUsn;

    /// <summary>
    /// If the read <see cref="Succeeded"/>, the list of records retrieved.
    /// </summary>
    public readonly IReadOnlyCollection<UsnRecord>? Records;

    /// <nodoc />
    public ReadUsnJournalResult(ReadUsnJournalStatus status, Usn nextUsn, IReadOnlyCollection<UsnRecord>? records)
    {
        Status = status;
        NextUsn = nextUsn;
        Records = records;
    }

    /// <summary>
    /// Indicates if reading the journal succeeded.
    /// </summary>
    public bool Succeeded => Status == ReadUsnJournalStatus.Success;
}
