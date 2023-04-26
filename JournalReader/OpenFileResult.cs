namespace JournalReader;

internal readonly struct OpenFileResult : IEquatable<OpenFileResult>
{
    /// <summary>
    /// Native error code.
    /// </summary>
    /// <remarks>
    /// This is the same as returned by <c>GetLastError</c>, except when it is not guaranteed to be set; then it is normalized to
    /// <c>ERROR_SUCCESS</c>
    /// </remarks>
    public int NativeErrorCode { get; }

    /// <summary>
    /// Normalized status indication (derived from <see cref="NativeErrorCode"/> and the creation disposition).
    /// </summary>
    /// <remarks>
    /// This is useful for two reasons: it is an enum for which we can know all cases are handled, and successful opens
    /// are always <see cref="OpenFileStatus.Success"/> (the distinction between opening / creating files is moved to
    /// <see cref="OpenedOrTruncatedExistingFile"/>)
    /// </remarks>
    public OpenFileStatus Status { get; }

    /// <summary>
    /// Indicates if an existing file was opened (or truncated). For creation dispositions such as <see cref="System.IO.FileMode.OpenOrCreate"/>,
    /// either value is possible on success. On failure, this is always <c>false</c> since no file was opened.
    /// </summary>
    public bool OpenedOrTruncatedExistingFile { get; }

    /// <summary>
    /// The path of the file that was opened. Null if the path was opened by <see cref="FileId"/>.
    /// </summary>
    public string? Path { get; }

    /// <summary>
    /// Creates an <see cref="OpenFileResult"/> without any normalization from native error code.
    /// </summary>
    private OpenFileResult(string? path, OpenFileStatus status, int nativeErrorCode, bool openedOrTruncatedExistingFile)
    {
        Path = path;
        Status = status;
        NativeErrorCode = nativeErrorCode;
        OpenedOrTruncatedExistingFile = openedOrTruncatedExistingFile;
    }

    /// <summary>
    /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
    /// Used when opening files by <see cref="FileId"/> to handle quirky error codes.
    /// </summary>
    public static OpenFileResult CreateForOpeningById(int nativeErrorCode, FileMode creationDisposition, bool handleIsValid)
    {
        return Create(path: null, nativeErrorCode, creationDisposition, handleIsValid, openingById: true);
    }

    /// <summary>
    /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
    /// </summary>
    public static OpenFileResult Create(string path, int nativeErrorCode, FileMode creationDisposition, bool handleIsValid)
    {
        return Create(path, nativeErrorCode, creationDisposition, handleIsValid, openingById: false);
    }

    /// <summary>
    /// Creates an <see cref="OpenFileResult"/> from observed return values from a native function.
    /// </summary>
    /// <remarks>
    /// <paramref name="openingById"/> is needed since <c>OpenFileById</c> has some quirky error codes.
    /// </remarks>
    private static OpenFileResult Create(string? path, int nativeErrorCode, FileMode creationDisposition, bool handleIsValid, bool openingById)
    {
        // Here's a handy table of various FileModes, corresponding dwCreationDisposition, and their properties:
        // See http://msdn.microsoft.com/en-us/library/windows/desktop/aa363858(v=vs.85).aspx
        // Managed FileMode | Creation disp.    | Error always set? | Distinguish existence?    | Existing file on success?
        // ----------------------------------------------------------------------------------------------------------------
        // Append           | OPEN_ALWAYS       | 1                 | 1                         | 0
        // Create           | CREATE_ALWAYS     | 1                 | 1                         | 0
        // CreateNew        | CREATE_NEW        | 0                 | 0                         | 0
        // Open             | OPEN_EXISTING     | 0                 | 0                         | 1
        // OpenOrCreate     | OPEN_ALWAYS       | 1                 | 1                         | 0
        // Truncate         | TRUNCATE_EXISTING | 0                 | 0                         | 1
        //
        // Note that some modes always set a valid last-error, and those are the same modes
        // that distinguish existence on success (i.e., did we just create a new file or did we open one).
        // The others do not promise to set ERROR_SUCCESS and instead failure implies existence
        // (or absence) according to the 'Existing file on success?' column.
        bool modeDistinguishesExistence =
            creationDisposition == FileMode.OpenOrCreate ||
            creationDisposition == FileMode.Create ||
            creationDisposition == FileMode.Append;

        if (handleIsValid && !modeDistinguishesExistence)
        {
            nativeErrorCode = NativeIOConstants.ErrorSuccess;
        }

        OpenFileStatus status;
        var openedOrTruncatedExistingFile = false;

        switch (nativeErrorCode)
        {
            case NativeIOConstants.ErrorSuccess:
                status = OpenFileStatus.Success;
                openedOrTruncatedExistingFile = creationDisposition == FileMode.Open || creationDisposition == FileMode.Truncate;
                break;
            case NativeIOConstants.ErrorFileNotFound:
                status = OpenFileStatus.FileNotFound;
                break;
            case NativeIOConstants.ErrorPathNotFound:
                status = OpenFileStatus.PathNotFound;
                break;
            case NativeIOConstants.ErrorAccessDenied:
                status = OpenFileStatus.AccessDenied;
                break;
            case NativeIOConstants.ErrorSharingViolation:
                status = OpenFileStatus.SharingViolation;
                break;
            case NativeIOConstants.ErrorLockViolation:
                status = OpenFileStatus.LockViolation;
                break;
            case NativeIOConstants.ErrorNotReady:
                status = OpenFileStatus.ErrorNotReady;
                break;
            case NativeIOConstants.FveLockedVolume:
                status = OpenFileStatus.FveLockedVolume;
                break;
            case NativeIOConstants.ErrorInvalidParameter:
                // Experimentally, it seems OpenFileById throws ERROR_INVALID_PARAMETER if the file ID doesn't exist.
                // This is very unfortunate, since that is also used for e.g. invalid sizes for FILE_ID_DESCRIPTOR. Oh well.
                status = openingById ? OpenFileStatus.FileNotFound : OpenFileStatus.UnknownError;
                break;
            case NativeIOConstants.ErrorFileExists:
            case NativeIOConstants.ErrorAlreadyExists:
                if (!handleIsValid)
                {
                    status = OpenFileStatus.FileAlreadyExists;
                }
                else
                {
                    status = OpenFileStatus.Success;
                    openedOrTruncatedExistingFile = true;
                }

                break;
            case NativeIOConstants.ErrorTimeout:
                status = OpenFileStatus.Timeout;
                break;
            case NativeIOConstants.ErrorCantAccessFile:
                status = OpenFileStatus.CannotAccessFile;
                break;
            case NativeIOConstants.ErrorBadPathname:
                status = OpenFileStatus.BadPathname;
                break;
            default:
                status = OpenFileStatus.UnknownError;
                break;
        }

        return new OpenFileResult(path, status, nativeErrorCode, openedOrTruncatedExistingFile);
    }

    /// <inheritdoc />
    public bool Succeeded => Status == OpenFileStatus.Success;

    /// <inheritdoc />
    public bool Equals(OpenFileResult other) =>
        other.NativeErrorCode == NativeErrorCode
        && other.OpenedOrTruncatedExistingFile == OpenedOrTruncatedExistingFile
        && other.Status == Status;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OpenFileResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => NativeErrorCode + (OpenedOrTruncatedExistingFile ? 1 : 0) | ((short)Status << 16);

    /// <inheritdoc />
    public static bool operator ==(OpenFileResult left, OpenFileResult right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(OpenFileResult left, OpenFileResult right) => !left.Equals(right);
}

internal enum OpenFileStatus : byte
{
    /// <summary>
    /// The file was opened (a valid handle was obtained).
    /// </summary>
    /// <remarks>
    /// The <see cref="OpenFileResult.NativeErrorCode"/> may be something other than <c>ERROR_SUCCESS</c>,
    /// since some open modes indicate if a file existed already or was created new via a special error code.
    /// </remarks>
    Success,

    /// <summary>
    /// The file was not found, and no handle was obtained.
    /// </summary>
    FileNotFound,

    /// <summary>
    /// Some directory component in the path was not found, and no handle was obtained.
    /// </summary>
    PathNotFound,

    /// <summary>
    /// The file was opened already with an incompatible share mode, and no handle was obtained.
    /// </summary>
    SharingViolation,

    /// <summary>
    /// The file cannot be opened with the requested access level, and no handle was obtained.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The file already exists (and the open mode specifies failure for existent files); no handle was obtained.
    /// </summary>
    FileAlreadyExists,

    /// <summary>
    /// The device the file is on is not ready. Should be treated as a nonexistent file.
    /// </summary>
    ErrorNotReady,

    /// <summary>
    /// The volume the file is on is locked. Should be treated as a nonexistent file.
    /// </summary>
    FveLockedVolume,

    /// <summary>
    /// The operaiton timed out. This generally occurs because of remote file materialization taking too long in the
    /// filter driver stack. Waiting and retrying may help.
    /// </summary>
    Timeout,

    /// <summary>
    /// The file cannot be accessed by the system. Should be treated as a nonexistent file.
    /// </summary>
    CannotAccessFile,

    /// <summary>
    /// The specified path is invalid. (from 'winerror.h')
    /// </summary>
    BadPathname,

    /// <summary>
    /// Cannot access the file because another process has locked a portion of the file.
    /// </summary>
    LockViolation,

    /// <summary>
    /// See <see cref="OpenFileResult.NativeErrorCode"/>
    /// </summary>
    UnknownError,
}

/// <summary>
/// Extensions to OpenFileStatus
/// </summary>
#pragma warning disable SA1649 // File name should match first type name
internal static class OpenFileStatusExtensions
#pragma warning restore SA1649
{
    /// <summary>
    /// Whether the status is one that should be treated as a nonexistent file
    /// </summary>
    /// <remarks>
    /// CODESYNC: <see cref="Windows.FileSystemWin.IsHresultNonexistent(int)"/>
    /// </remarks>
    public static bool IsNonexistent(this OpenFileStatus status) =>
        status == OpenFileStatus.FileNotFound
        || status == OpenFileStatus.PathNotFound
        || status == OpenFileStatus.ErrorNotReady
        || status == OpenFileStatus.FveLockedVolume
        || status == OpenFileStatus.CannotAccessFile
        || status == OpenFileStatus.BadPathname;

    /// <summary>
    /// Whether the status is one that implies other process blocking the handle.
    /// </summary>
    public static bool ImpliesOtherProcessBlockingHandle(this OpenFileStatus status) => 
        status == OpenFileStatus.SharingViolation
        || status == OpenFileStatus.AccessDenied
        || status == OpenFileStatus.LockViolation;
}
