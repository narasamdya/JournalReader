using Microsoft.Win32.SafeHandles;
using System.Diagnostics;

namespace JournalReader;

internal sealed class JournalAccessor
{
    /// <summary>
    /// Buffer size for FSCTL_READ_USN_JOURNAL.
    /// Journal records are about 100 bytes each, so this gets about 655 records per read.
    /// </summary>
    private const int JournalReadBufferSize = 64 * 1024;

    /// <summary>
    /// Whether the journal operations are privileged or unprivileged based on the OS version
    /// </summary>
    /// <remarks>
    /// Win10-RS2 has a separate unprivileged journal operation whose ioControlCode is different.
    /// This property will decide which ioControlCode will be used to scan the journal.
    /// Also, trailing slash in the volume guid path matters for unprivileged and privileged read journal operations.
    /// </remarks>
    public bool IsJournalUnprivileged { get; set; }

    public static Possible<JournalAccessor> GetJournalAccessor(VolumeMap volumeMap, string path)
    {
        var journalAccessor = new JournalAccessor();

        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            if (volumeMap.TryGetVolumePathForHandle(file.SafeFileHandle, out VolumeGuidPath volume) && volume.IsValid)
            {
                // Journal accessor needs to know whether the OS allows unprivileged journal operations
                // because some operation names (e.g., reading journal, getting volume handle) are different.
                journalAccessor.IsJournalUnprivileged = !Native.IsProcessElevated();

                // Attempt to access journal. Any error means that the journal operations are not unprivileged yet in the host computer.
                var result = journalAccessor.QueryJournal(new QueryJournalRequest(volume));

                if (!result.IsError)
                {
                    return result.Response.Succeeded
                        ? journalAccessor
                        : new Failure<string>($"Querying journal result for '{path}' results in {result.Response.Status}");
                }
                else
                {
                    return new Failure<string>(result.Error.Message);
                }
            }

            return new Failure<string>($"Failed to get volume path for '{path}'");
        }
        catch (Exception e)
        {
            return new Failure<string>($"Failed to create journal accessor for '{path}': {e}");
        }
    }

    /// <inheritdoc />
    public MaybeResponse<ReadJournalResponse> ReadJournal(ReadJournalRequest request, Action<UsnRecord> onUsnRecordReceived)
    {
        OpenFileResult volumeOpenResult = OpenVolumeHandle(request.VolumeGuidPath, out SafeFileHandle? volumeHandle);

        using (volumeHandle)
        {
            if (!volumeOpenResult.Succeeded)
            {
                return new MaybeResponse<ReadJournalResponse>(
                    new ErrorResponse(ErrorStatus.FailedToOpenVolumeHandle,
                    $"Failed to open a volume handle for the volume '{request.VolumeGuidPath.Path}'"));
            }

            Usn startUsn = request.StartUsn;
            Usn endUsn = request.EndUsn ?? Usn.Zero;
            int extraReadCount = request.ExtraReadCount ?? -1;
            long timeLimitInTicks = request.TimeLimit?.Ticks ?? -1;
            var sw = Stopwatch.StartNew();

            byte[] buffer = new byte[JournalReadBufferSize];
            while (true)
            {
                if (timeLimitInTicks >= 0 && timeLimitInTicks < sw.ElapsedTicks)
                {
                    return new MaybeResponse<ReadJournalResponse>(new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: startUsn, timeout: true));
                }

                ReadUsnJournalResult result = Native.TryReadUsnJournal(
                    volumeHandle!,
                    buffer,
                    request.JournalId,
                    startUsn,
                    isJournalUnprivileged: IsJournalUnprivileged);

                if (!result.Succeeded)
                {
                    // Bug #1164760 shows that the next USN can be non-zero.
                    return new MaybeResponse<ReadJournalResponse>(new ReadJournalResponse(status: result.Status, nextUsn: result.NextUsn));
                }

                if (result.Records!.Count == 0)
                {
                    return new MaybeResponse<ReadJournalResponse>(new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: result.NextUsn));
                }

                foreach (var record in result.Records)
                {
                    onUsnRecordReceived(record);
                }

                startUsn = result.NextUsn;

                if (!endUsn.IsZero)
                {
                    if (startUsn >= endUsn && (--extraReadCount) < 0)
                    {
                        return new MaybeResponse<ReadJournalResponse>(new ReadJournalResponse(status: ReadUsnJournalStatus.Success, nextUsn: result.NextUsn));
                    }
                }
            }
        }
    }

    public MaybeResponse<QueryUsnJournalResult> QueryJournal(QueryJournalRequest request)
    {
        OpenFileResult openVolumeResult = OpenVolumeHandle(request.VolumeGuidPath, out SafeFileHandle? volumeHandle);

        using (volumeHandle)
        {
            if (!openVolumeResult.Succeeded)
            {
                return new MaybeResponse<QueryUsnJournalResult>(
                    new ErrorResponse(ErrorStatus.FailedToOpenVolumeHandle,
                    $"Failed to open a volume handle for the volume '{request.VolumeGuidPath.Path}': Status: {openVolumeResult.Status} | Error code: {openVolumeResult.NativeErrorCode}"));
            }

            QueryUsnJournalResult result = Native.TryQueryUsnJournal(volumeHandle!);
            return new MaybeResponse<QueryUsnJournalResult>(result);
        }
    }

    public static Possible<(FileIdAndVolumeId, UsnRecord)> GetVersionedFileIdentityByHandle(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        return GetVersionedFileIdentityByHandle(stream.SafeFileHandle);
    }

    public static Possible<(FileIdAndVolumeId, UsnRecord)> GetVersionedFileIdentityByHandle(SafeFileHandle fileHandle)
    {
        try
        {
            UsnRecord? usnRecord = Native.ReadFileUsnByHandle(fileHandle);

            // If usnRecord is null or 0, then fail!
            if (!usnRecord.HasValue || usnRecord.Value.Usn.IsZero)
            {
                return new Failure<string>("USN record is null or 0");
            }

            FileIdAndVolumeId? maybeIds = Native.GetFileIdAndVolumeIdByHandleOrNull(fileHandle);

            // A short volume serial isn't the first choice (fewer random bits), but we fall back to it if the long serial is unavailable.
            var volumeSerial = maybeIds.HasValue ? maybeIds.Value.VolumeSerialNumber : Native.GetShortVolumeSerialNumberByHandle(fileHandle);

            return (new FileIdAndVolumeId(volumeSerial, usnRecord.Value.FileId), usnRecord.Value);
        }
        catch (Exception e)
        {
            return new Failure<string>(e.Message);
        }
    }

    private OpenFileResult OpenVolumeHandle(VolumeGuidPath path, out SafeFileHandle? volumeHandle)
    {
        return Native.TryOpenDirectory(
            IsJournalUnprivileged ? path.Path : path.GetDevicePath(),
            FileDesiredAccess.GenericRead,
            FileShare.ReadWrite | FileShare.Delete,
            FileMode.Open,
            FileFlagsAndAttributes.None,
            out volumeHandle);
    }

    internal sealed class ReadJournalRequest
    {
        /// <summary>
        /// Change journal identifier
        /// </summary>
        /// <remarks>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365720(v=vs.85).aspx
        /// </remarks>
        public readonly ulong JournalId;

        /// <summary>
        /// Start cursor (or 0 for the first available record).
        /// </summary>
        public readonly Usn StartUsn;

        /// <summary>
        /// End cursor.
        /// </summary>
        public readonly Usn? EndUsn;

        public VolumeGuidPath VolumeGuidPath { init; get; }

        /// <summary>
        /// Extra read count after reading the journal exceeds <see cref="EndUsn"/> if <see cref="EndUsn"/> is specified.
        /// </summary>
        public readonly int? ExtraReadCount;

        /// <summary>
        /// Time limit for reading journal.
        /// </summary>
        public readonly TimeSpan? TimeLimit;

        /// <nodoc />
        public ReadJournalRequest(
            VolumeGuidPath volumeGuidPath,
            ulong journalId,
            Usn startUsn,
            Usn? endUsn = default,
            int? extraReadCount = default,
            TimeSpan? timeLimit = default)
        {
            VolumeGuidPath = volumeGuidPath;
            JournalId = journalId;
            StartUsn = startUsn;
            EndUsn = endUsn;
            ExtraReadCount = extraReadCount;
            TimeLimit = timeLimit;
        }
    }

    internal sealed class ReadJournalResponse
    {
        /// <summary>
        /// The next USN that should be read if trying to read more of the journal at a later time.
        /// </summary>
        public readonly Usn NextUsn;

        /// <summary>
        /// Status indication of the read attempt.
        /// </summary>
        public readonly ReadUsnJournalStatus Status;

        /// <summary>
        /// Indicates if journal scanning reached timeout.
        /// </summary>
        public readonly bool Timeout;

        /// <nodoc />
        public ReadJournalResponse(Usn nextUsn, ReadUsnJournalStatus status, bool timeout = false)
        {
            NextUsn = nextUsn;
            Status = status;
            Timeout = timeout;
        }
    }

    internal sealed class QueryJournalRequest
    {
        public VolumeGuidPath VolumeGuidPath { init; get; }

        /// <nodoc />
        public QueryJournalRequest(VolumeGuidPath volumeGuidPath) => VolumeGuidPath = volumeGuidPath;
    }

    /// <summary>
    /// Wrapper for a response which is either <typeparamref name="TResponse" /> (on success) or <see cref="ErrorResponse" />
    /// (on failure).
    /// </summary>
    public readonly struct MaybeResponse<TResponse>
        where TResponse : class
    {
        private readonly object _value;

        /// <summary>
        /// Creates a 'maybe' wrapper representing success.
        /// </summary>
        public MaybeResponse(TResponse successResponse)
        {
            _value = successResponse;
        }

        /// <summary>
        /// Creates a 'maybe' wrapper representing failure.
        /// </summary>
        public MaybeResponse(ErrorResponse errorResponse)
        {
            _value = errorResponse;
        }

        /// <summary>
        /// Indicates if this is an error response. If true, <see cref="Error" /> is available.
        /// Otherwise, <see cref="Response" /> is available.
        /// </summary>
        public bool IsError => _value is ErrorResponse;

        /// <summary>
        /// Success response if <c>IsError == false</c>.
        /// </summary>
        public TResponse Response => (TResponse)_value;

        /// <summary>
        /// Success response if <c>IsError == true</c>.
        /// </summary>
        public ErrorResponse Error => (ErrorResponse) _value;
    }

    /// <summary>
    /// Error codes specific to using the change journal service (vs. errors that would be present if
    /// accessing the change journal directly).
    /// </summary>
    public enum ErrorStatus
    {
        /// <summary>
        /// A response was ill-formed, indicating a client bug.
        /// </summary>
        ProtocolError,

        /// <summary>
        /// A specified volume GUID path was not accessible.
        /// </summary>
        FailedToOpenVolumeHandle,
    }

    /// <summary>
    /// Service response indicating a failure condition.
    /// </summary>
    public sealed class ErrorResponse
    {
        /// <summary>
        /// Error class
        /// </summary>
        public ErrorStatus Status { get; }

        /// <summary>
        /// Additional message for diagnostics.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Creates an error response.
        /// </summary>
        public ErrorResponse(ErrorStatus status, string message)
        {
            Message = message;
            Status = status;
        }
    }

}
