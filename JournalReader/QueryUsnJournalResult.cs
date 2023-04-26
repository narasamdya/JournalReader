namespace JournalReader;

internal sealed class QueryUsnJournalResult
{
    /// <summary>
    /// Status indication of the query attempt.
    /// </summary>
    public readonly QueryUsnJournalStatus Status;

    private readonly QueryUsnJournalData? _data;

    /// <nodoc />
    public QueryUsnJournalResult(QueryUsnJournalStatus status, QueryUsnJournalData? data)
    {
        Status = status;
        _data = data;
    }

    /// <summary>
    /// Indicates if querying the journal succeeded.
    /// </summary>
    public bool Succeeded => Status == QueryUsnJournalStatus.Success;

    /// <summary>
    /// Returns the queried data (fails if not <see cref="Succeeded"/>).
    /// </summary>
    internal QueryUsnJournalData? Data => _data;
}
