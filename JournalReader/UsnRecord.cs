namespace JournalReader;

/// <summary>
/// Represents USN data from reading a journal.
/// </summary>
/// <remarks>
/// This is the managed projection of the useful fields of <see cref="Windows.FileSystemWin.NativeUsnRecordV3"/>.
/// It does not correspond to any actual native structure.
/// Note that this record may be invalid. A record is invalid if it has Usn 0, which indicates
/// that either the volume's change journal is disabled or that this particular file has not
/// been modified since the change journal was enabled.
/// </remarks>
internal readonly struct UsnRecord : IEquatable<UsnRecord>
{
    /// <summary>
    /// ID of the file to which this record pertains
    /// </summary>
    public readonly FileId FileId;

    /// <summary>
    /// ID of the containing directory of the file at the time of this change.
    /// </summary>
    public readonly FileId ContainerFileId;

    /// <summary>
    /// Change journal cursor at which this record sits.
    /// </summary>
    public readonly Usn Usn;

    /// <summary>
    /// Reason for the change.
    /// </summary>
    public readonly UsnChangeReasons Reason;

    /// <summary>
    /// Timestamp.
    /// </summary>
    public readonly long Timestamp;

    /// <summary>
    /// File name.
    /// </summary>
    public readonly string FileName;

    /// <summary>
    /// Creates a UsnRecord
    /// </summary>
    public UsnRecord(FileId file, FileId container, Usn usn, UsnChangeReasons reasons, long timestamp, string fileName)
    {
        FileId = file;
        ContainerFileId = container;
        Usn = usn;
        Reason = reasons;
        Timestamp = timestamp;
        FileName = fileName;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UsnRecord other && Equals(other);

    /// <inheritdoc />
    public bool Equals(UsnRecord other) =>
        FileId == other.FileId
        && Usn == other.Usn
        && Reason == other.Reason
        && ContainerFileId == other.ContainerFileId
        && Timestamp == other.Timestamp
        && string.Equals(FileName, other.FileName, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => (FileId, Usn, (int)Reason, ContainerFileId, Timestamp, FileName).GetHashCode();

    /// <inheritdoc />
    public static bool operator ==(UsnRecord left, UsnRecord right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(UsnRecord left, UsnRecord right) => !left.Equals(right);
}
