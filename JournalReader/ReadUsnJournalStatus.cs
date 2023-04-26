﻿namespace JournalReader;

internal enum ReadUsnJournalStatus : byte
{
    /// <summary>
    /// Reading the journal succeeded. Zero or more records have been retrieved.
    /// </summary>
    Success,

    /// <summary>
    /// The journal on the specified volume is not active.
    /// </summary>
    JournalNotActive,

    /// <summary>
    /// The journal on the specified volume is being deleted (a later read would return <see cref="JournalNotActive"/>).
    /// </summary>
    JournalDeleteInProgress,

    /// <summary>
    /// There is a valid journal, but the specified <see cref="ReadUsnJournalData.StartUsn"/> has been truncated out of it.
    /// Consider specifying a start USN of 0 to get the earliest available records.
    /// </summary>
    JournalEntryDeleted,

    /// <summary>
    /// Incorrect parameter error happens when the volume format is broken.
    /// </summary>
    InvalidParameter,

    /// <summary>
    /// The queried volume does not support writing a change journal.
    /// </summary>
    VolumeDoesNotSupportChangeJournals,
}
