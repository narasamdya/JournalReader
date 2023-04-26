using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Text;

namespace JournalReader;

internal class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public uint PropertyId;
        public uint QueryType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] AdditionalParameters;
    }

    private const uint StorageDeviceSeekPenaltyProperty = 7;
    private const uint PropertyStandardQuery = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public readonly uint Version;
        public readonly uint Size;
        [MarshalAs(UnmanagedType.U1)]
        public readonly bool IncursSeekPenalty;
    }

    /// <summary>
    /// Union tag for <see cref="FileIdDescriptor"/>.
    /// </summary>
    /// <remarks>
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa364227(v=vs.85).aspx
    /// </remarks>
    private enum FileIdDescriptorType
    {
        FileId = 0,

        // ObjectId = 1, - Not supported
        ExtendedFileId = 2,
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileIdDescriptor
    {
        private static readonly int s_size = Marshal.SizeOf<FileIdDescriptor>();

        public readonly int Size;
        public readonly FileIdDescriptorType Type;
        public readonly FileId ExtendedFileId;

        public FileIdDescriptor(FileId fileId)
        {
            Type = FileIdDescriptorType.ExtendedFileId;
            Size = s_size;
            ExtendedFileId = fileId;
        }
    }

    /// <summary>
    /// Header data in common between USN_RECORD_V2 and USN_RECORD_V3. These fields are needed to determine how to interpret a returned record.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeUsnRecordHeader
    {
        /// <summary>
        /// Size of the record header in bytes.
        /// </summary>
        public static readonly int Size = Marshal.SizeOf<NativeUsnRecordHeader>();

        public readonly int RecordLength;
        public readonly ushort MajorVersion;
        public readonly ushort MinorVersion;
    }

    /// <summary>
    /// USN_RECORD_V3
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802708(v=vs.85).aspx
    /// </summary>
    /// <remarks>
    /// The Size is explicitly set to the actual used size + the needing padding to 8-byte alignment
    /// (for Usn, Timestamp, etc.). Two of those padding bytes are actually the first character of the filename.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 0x50)]
    private unsafe readonly struct NativeUsnRecordV3
    {
        /// <summary>
        /// Size of a record with two filename characters (starting at WCHAR FileName[1]; not modeled in the C# struct),
        /// or one filename character and two bytes of then-needed padding (zero-length filenames are disallowed).
        /// This is the minimum size that should ever be returned.
        /// </summary>
        public static readonly int MinimumSize = Marshal.SizeOf<NativeUsnRecordV3>();

        /// <summary>
        /// Maximum size of a single V3 record, assuming the NTFS / ReFS 255 character file name length limit.
        /// </summary>
        /// <remarks>
        /// ( (MaximumComponentLength - 1) * sizeof(WCHAR) + sizeof(USN_RECORD_V3)
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802708(v=vs.85).aspx
        /// Due to padding this is perhaps an overestimate.
        /// </remarks>
        public static readonly int MaximumSize = MinimumSize + (254 * 2);

        public readonly NativeUsnRecordHeader Header;
        public readonly FileId FileReferenceNumber;
        public readonly FileId ParentFileReferenceNumber;
        public readonly Usn Usn;
        public readonly long TimeStamp;
        public readonly uint Reason;
        public readonly uint SourceInfo;
        public readonly uint SecurityId;
        public readonly uint FileAttributes;
        public readonly ushort FileNameLength;
        public readonly ushort FileNameOffset;
        // public readonly char* FileName;

        // WCHAR FileName[1];
    }

    /// <summary>
    /// USN_RECORD_V2
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365722(v=vs.85).aspx
    /// </summary>
    /// <remarks>
    /// The Size is explicitly set to the actual used size + the needing padding to 8-byte alignment
    /// (for Usn, Timestamp, etc.). Two of those padding bytes are actually the first character of the filename.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Size = 0x40)]
    private unsafe readonly struct NativeUsnRecordV2
    {
        /// <summary>
        /// Size of a record with two filename characters (starting at WCHAR FileName[1]; not modeled in the C# struct),
        /// or one filename character and two bytes of then-needed padding (zero-length filenames are disallowed).
        /// This is the minimum size that should ever be returned.
        /// </summary>
        public static readonly int MinimumSize = Marshal.SizeOf<NativeUsnRecordV2>();

        /// <summary>
        /// Maximum size of a single V2 record, assuming the NTFS / ReFS 255 character file name length limit.
        /// </summary>
        /// <remarks>
        /// ( (MaximumComponentLength - 1) * sizeof(WCHAR) + sizeof(USN_RECORD_V2)
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365722(v=vs.85).aspx
        /// Due to padding this is perhaps an overestimate.
        /// </remarks>
        public static readonly int MaximumSize = MinimumSize + (254 * 2);

        public readonly NativeUsnRecordHeader Header;
        public readonly ulong FileReferenceNumber;
        public readonly ulong ParentFileReferenceNumber;
        public readonly Usn Usn;
        public readonly long TimeStamp;
        public readonly uint Reason;
        public readonly uint SourceInfo;
        public readonly uint SecurityId;
        public readonly uint FileAttributes;
        public readonly ushort FileNameLength;
        public readonly ushort FileNameOffset;
        // public readonly char* FileName;

        // WCHAR FileName[1];
    }

    /// <summary>
    /// Request structure indicating this program's supported version range of Usn records.
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802705(v=vs.85).aspx
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ReadFileUsnData
    {
        /// <summary>
        /// Size of this structure (there are no variable length fields).
        /// </summary>
        public static readonly int Size = Marshal.SizeOf<ReadFileUsnData>();

        /// <summary>
        /// Indicates that FSCTL_READ_FILE_USN_DATA should return either V2 or V3 records (those with NTFS or ReFS-sized file IDs respectively).
        /// </summary>
        /// <remarks>
        /// This request should work on Windows 8 / Server 2012 and above.
        /// </remarks>
        public static readonly ReadFileUsnData NtfsAndReFSCompatible = new()
        {
            MinMajorVersion = 2,
            MaxMajorVersion = 3,
        };

        /// <summary>
        /// Indicates that FSCTL_READ_FILE_USN_DATA should return only V2 records (those with NTFS file IDs, even if using ReFS).
        /// </summary>
        /// <remarks>
        /// This request should work on Windows 8 / Server 2012 and above.
        /// </remarks>
        public static readonly ReadFileUsnData NtfsCompatible = new()
        {
            MinMajorVersion = 2,
            MaxMajorVersion = 2,
        };

        public ushort MinMajorVersion;
        public ushort MaxMajorVersion;
    }

    internal sealed class SafeFindVolumeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeFindVolumeHandle() : base(ownsHandle: true) {  }

        protected override bool ReleaseHandle() => FindVolumeClose(handle);
    }

    /// <summary>
    /// FILE_INFO_BY_HANDLE_CLASS for GetFileInformationByHandleEx.
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa364953(v=vs.85).aspx
    /// </summary>
    private enum FileInfoByHandleClass : uint
    {
        FileBasicInfo = 0x0,
        FileStandardInfo = 0x1,
        FileNameInfo = 0x2,
        FileRenameInfo = 0x3,
        FileDispositionInfo = 0x4,
        FileAllocationInfo = 0x5,
        FileEndOfFileInfo = 0x6,
        FileStreamInfo = 0x7,
        FileCompressionInfo = 0x8,
        FileAttributeTagInfo = 0x9,
        FileIdBothDirectoryInfo = 0xa,
        FileIdBothDirectoryRestartInfo = 0xb,
        FileRemoteProtocolInfo = 0xd,
        FileFullDirectoryInfo = 0xe,
        FileFullDirectoryRestartInfo = 0xf,
        FileStorageInfo = 0x10,
        FileAlignmentInfo = 0x11,
        FileIdInfo = 0x12,
        FileIdExtdDirectoryInfo = 0x13,
        FileIdExtdDirectoryRestartInfo = 0x14,
        FileDispositionInfoEx = 0x15,
        FileRenameInfoEx = 0x16,
    }

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenUser = 1,
        TokenGroups,
        TokenPrivileges,
        TokenOwner,
        TokenPrimaryGroup,
        TokenDefaultDacl,
        TokenSource,
        TokenType,
        TokenImpersonationLevel,
        TokenStatistics,
        TokenRestrictedSids,
        TokenSessionId,
        TokenGroupsAndPrivileges,
        TokenSessionReference,
        TokenSandBoxInert,
        TokenAuditPolicy,
        TokenOrigin,
        TokenElevationType,
        TokenLinkedToken,
        TokenElevation,
        TokenHasRestrictions,
        TokenAccessInformation,
        TokenVirtualizationAllowed,
        TokenVirtualizationEnabled,
        TokenIntegrityLevel,
        TokenUIAccess,
        TokenMandatoryPolicy,
        TokenLogonSid,
        TokenIsAppContainer,
        TokenCapabilities,
        TokenAppContainerSid,
        TokenAppContainerNumber,
        TokenUserClaimAttributes,
        TokenDeviceClaimAttributes,
        TokenRestrictedUserClaimAttributes,
        TokenRestrictedDeviceClaimAttributes,
        TokenDeviceGroups,
        TokenRestrictedDeviceGroups,
        TokenSecurityAttributes,
        TokenIsRestricted,
        MaxTokenInfoClass,
    }

    public const uint STANDARD_RIGHTS_READ = 0x00020000;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TOKEN_READ = STANDARD_RIGHTS_READ | TOKEN_QUERY;

    public struct TOKEN_ELEVATION
    {
        /// <nodoc />
        public int TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        FileDesiredAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileMode dwCreationDisposition,
        FileFlagsAndAttributes dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hFile, // Any handle on the relevant volume
        [In] FileIdDescriptor lpFileId,
        FileDesiredAccess dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        FileFlagsAndAttributes dwFlagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle deviceHandle,
        uint ioControlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        IntPtr outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle deviceHandle,
        uint ioControlCode,
        IntPtr inputBuffer,
        int inputBufferSize,
        [Out] QueryUsnJournalData outputBuffer,
        int outputBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint ioControlCode,
        ref STORAGE_PROPERTY_QUERY inputBuffer,
        int inputBufferSize,
        out DEVICE_SEEK_PENALTY_DESCRIPTOR outputBuffer,
        int outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFindVolumeHandle FindFirstVolumeW([Out] StringBuilder volumeNameBuffer, int volumeNameBufferLength);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNextVolumeW(SafeFindVolumeHandle findVolumeHandle, [Out] StringBuilder volumeNameBuffer, int volumeNameBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FindVolumeClose(IntPtr findVolumeHandle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationByHandleW(
        SafeFileHandle fileHandle,
        [Out] StringBuilder? volumeNameBuffer,
        int volumeNameBufferSize,
        IntPtr volumeSerial,
        IntPtr maximumComponentLength,
        IntPtr fileSystemFlags,
        [Out] StringBuilder? fileSystemNameBuffer,
        int fileSystemNameBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNamesForVolumeNameW(
        [MarshalAs(UnmanagedType.LPWStr)]
        string volumeName,
        [Out] StringBuilder volumeNamePathBuffer,
        int bufferLength,
        out int length);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPointW(string volumeMountPoint, [Out] StringBuilder volumeName, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle deviceHandle, uint fileInformationClass, IntPtr outputFileInformationBuffer, int outputBufferSize);

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern int GetThreadErrorMode();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadErrorMode(int newErrorMode, out int oldErrorMode);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TOKEN_INFORMATION_CLASS tokenInformationClass,
        IntPtr tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);

    public static List<(VolumeGuidPath, ulong)> ListVolumeGuidPathsAndSerials()
    {

        var volumeList = new List<(VolumeGuidPath, ulong)>();

        // We don't want funky message boxes for poking removable media, e.g. a CD drive without a disk.
        // By observation, these drives *may* be returned when enumerating volumes. Run 'wmic volume get DeviceId,Name'
        // when an empty floppy / cd drive is visible in explorer.
        using (ErrorModeContext.DisableMessageBoxForRemovableMedia())
        {
            var volumeNameBuffer = new StringBuilder(capacity: NativeIOConstants.MaxPath + 1);
            using (SafeFindVolumeHandle findVolumeHandle = FindFirstVolumeW(volumeNameBuffer, volumeNameBuffer.Capacity))
            {
                {
                    int hr = Marshal.GetLastWin32Error();

                    // The docs say we'll see an invalid handle if it 'fails to find any volumes'. It's very hard to run this program without a volume, though.
                    // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364425(v=vs.85).aspx
                    if (findVolumeHandle.IsInvalid)
                    {
                        throw new Win32Exception(hr, "FindNextVolumeW");
                    }
                }

                do
                {
                    string volumeGuidPathString = volumeNameBuffer.ToString();
                    volumeNameBuffer.Clear();

                    bool volumeGuidPathParsed = VolumeGuidPath.TryCreate(volumeGuidPathString, out VolumeGuidPath volumeGuidPath);

                    if (volumeGuidPathParsed
                        && TryOpenDirectory(volumeGuidPathString, FileDesiredAccess.None, FileShare.Delete | FileShare.Read | FileShare.Write, FileMode.Open, FileFlagsAndAttributes.None, out SafeFileHandle? volumeRoot).Succeeded)
                    {
                        ulong serial;
                        using (volumeRoot)
                        {
                            serial = GetVolumeSerialNumberByHandle(volumeRoot!);
                        }

                        volumeList.Add((volumeGuidPath, serial));
                    }
                }
                while (FindNextVolumeW(findVolumeHandle, volumeNameBuffer, volumeNameBuffer.Capacity));

                // FindNextVolumeW returned false; hopefully for the right reason.
                {
                    int hr = Marshal.GetLastWin32Error();
                    if (hr != NativeIOConstants.ErrorNoMoreFiles)
                    {
                        throw new Win32Exception(hr, "FindNextVolumeW");
                    }
                }
            }
        }

        return volumeList;
    }

    public static Possible<(VolumeGuidPath volumeGuidPath, ulong serial), Failure<string>> GetVolumeGuidPathAndSerialOrFail(string volumeMountPoint)
    {
        try
        {
            return GetVolumeGuidPathAndSerial(volumeMountPoint);
        }
        catch (Exception e)
        {
            return new Failure<string>(e.Message);
        }
    }

    public static (VolumeGuidPath volumeGuidPath, ulong serial) GetVolumeGuidPathAndSerial(string volumeMountPoint)
    {
        var volumeNameBuffer = new StringBuilder(capacity: NativeIOConstants.MaxPath + 1);

        if (!GetVolumeNameForVolumeMountPointW(volumeMountPoint, volumeNameBuffer, volumeNameBuffer.Capacity))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetVolumeNameForVolumeMountPointW({volumeMountPoint})");
        }

        string volumeGuidPathString = volumeNameBuffer.ToString();
        if (!VolumeGuidPath.TryCreate(volumeGuidPathString, out VolumeGuidPath volumeGuidPath))
        {
            throw new InvalidDataException($"Unable to parse volume GUID path '{volumeGuidPathString}'.");
        }

        if (TryOpenDirectory(volumeGuidPathString, FileDesiredAccess.None, FileShare.Delete | FileShare.Read | FileShare.Write, FileMode.Open, FileFlagsAndAttributes.None, out SafeFileHandle? volumeRoot).Succeeded)
        {
            ulong serial;
            using (volumeRoot)
            {
                serial = GetVolumeSerialNumberByHandle(volumeRoot!);
            }

            return (volumeGuidPath, serial);
        }

        throw new IOException($"Unable to open volume GUID path '{volumeGuidPathString}'.");
    }

    public static unsafe ReadUsnJournalResult TryReadUsnJournal(
        SafeFileHandle volumeHandle,
        byte[] buffer,
        ulong journalId,
        Usn startUsn = default,
        bool forceJournalVersion2 = false,
        bool isJournalUnprivileged = false)
    {
        var readOptions = new ReadUsnJournalData
        {
            MinMajorVersion = 2,
            MaxMajorVersion = forceJournalVersion2 ? (ushort)2 : (ushort)3,
            StartUsn = startUsn,
            Timeout = 0,
            BytesToWaitFor = 0,
            ReasonMask = uint.MaxValue,
            ReturnOnlyOnClose = 0,
            UsnJournalID = journalId,
        };

        int bytesReturned;
        bool ioctlSuccess;
        int error;

        fixed (byte* pRecordBuffer = buffer)
        {
            ioctlSuccess = DeviceIoControl(
                volumeHandle,
                ioControlCode: isJournalUnprivileged ? NativeIOConstants.FsctlReadUnprivilegedUsnJournal : NativeIOConstants.FsctlReadUsnJournal,
                inputBuffer: (IntPtr)(&readOptions),
                inputBufferSize: ReadUsnJournalData.Size,
                outputBuffer: (IntPtr)pRecordBuffer,
                outputBufferSize: buffer.Length,
                bytesReturned: out bytesReturned,
                overlapped: IntPtr.Zero);
            error = Marshal.GetLastWin32Error();
        }

        if (!ioctlSuccess)
        {
            ReadUsnJournalStatus errorStatus;
            switch ((uint)error)
            {
                case NativeIOConstants.ErrorJournalNotActive:
                    errorStatus = ReadUsnJournalStatus.JournalNotActive;
                    break;
                case NativeIOConstants.ErrorJournalDeleteInProgress:
                    errorStatus = ReadUsnJournalStatus.JournalDeleteInProgress;
                    break;
                case NativeIOConstants.ErrorJournalEntryDeleted:
                    errorStatus = ReadUsnJournalStatus.JournalEntryDeleted;
                    break;
                case NativeIOConstants.ErrorInvalidParameter:
                    errorStatus = ReadUsnJournalStatus.InvalidParameter;
                    break;
                case NativeIOConstants.ErrorInvalidFunction:
                    errorStatus = ReadUsnJournalStatus.VolumeDoesNotSupportChangeJournals;
                    break;
                default:
                    throw new Win32Exception(error, "DeviceIoControl(FSCTL_READ_USN_JOURNAL)");
            }

            return new ReadUsnJournalResult(errorStatus, nextUsn: new Usn(0), records: null);
        }

        Contract.Assume(
            bytesReturned >= sizeof(ulong),
            "The output buffer should always contain the updated USN cursor (even if no records were returned)");

        var recordsToReturn = new List<UsnRecord>();
        ulong nextUsn;
        fixed (byte* recordBufferBase = buffer)
        {
            nextUsn = *(ulong*)recordBufferBase;
            byte* currentRecordBase = recordBufferBase + sizeof(ulong);
            Contract.Assume(currentRecordBase != null);

            // One past the end of the record part of the buffer
            byte* recordsEnd = recordBufferBase + bytesReturned;

            while (currentRecordBase < recordsEnd)
            {
                Contract.Assume(
                    currentRecordBase + NativeUsnRecordHeader.Size <= recordsEnd,
                    "Not enough data returned for a valid USN record header");

                NativeUsnRecordHeader* currentRecordHeader = (NativeUsnRecordHeader*)currentRecordBase;

                Contract.Assume(
                    currentRecordBase + currentRecordHeader->RecordLength <= recordsEnd,
                    "RecordLength field advances beyond the buffer");

                if (currentRecordHeader->MajorVersion == 3)
                {
                    Contract.Assume(!forceJournalVersion2);
                    if (!(currentRecordHeader->RecordLength >= NativeUsnRecordV3.MinimumSize &&
                         currentRecordHeader->RecordLength <= NativeUsnRecordV3.MaximumSize))
                    {
                        Contract.Assert(false, $"Size in record header does not correspond to a valid USN_RECORD_V3. Header record length: {currentRecordHeader->RecordLength} (valid length: {NativeUsnRecordV3.MinimumSize} <= length <= {NativeUsnRecordV3.MaximumSize})");
                    }

                    NativeUsnRecordV3* record = (NativeUsnRecordV3*)currentRecordBase;
                    string fileName = string.Empty; //  Encoding.Unicode.GetString(currentRecordBase + record->FileNameOffset, record->FileNameLength);

                    recordsToReturn.Add(
                        new UsnRecord(
                            record->FileReferenceNumber,
                            record->ParentFileReferenceNumber,
                            record->Usn,
                            (UsnChangeReasons)record->Reason,
                            record->TimeStamp,
                            fileName));
                }
                else if (currentRecordHeader->MajorVersion == 2)
                {
                    if (!(currentRecordHeader->RecordLength >= NativeUsnRecordV2.MinimumSize &&
                          currentRecordHeader->RecordLength <= NativeUsnRecordV2.MaximumSize))
                    {
                        Contract.Assert(false, $"Size in record header does not correspond to a valid USN_RECORD_V2. Header record length: {currentRecordHeader->RecordLength} (valid length: {NativeUsnRecordV2.MinimumSize} <= length <= {NativeUsnRecordV2.MaximumSize})");
                    }

                    NativeUsnRecordV2* record = (NativeUsnRecordV2*)currentRecordBase;
                    string fileName = string.Empty; //  Encoding.Unicode.GetString(currentRecordBase + record->FileNameOffset, record->FileNameLength);
                    recordsToReturn.Add(
                        new UsnRecord(
                            new FileId(0, record->FileReferenceNumber),
                            new FileId(0, record->ParentFileReferenceNumber),
                            record->Usn,
                            (UsnChangeReasons)record->Reason,
                            record->TimeStamp,
                            fileName));
                }
                else
                {
                    Contract.Assume(
                        false,
                        "An unrecognized record version was returned, even though version 2 or 3 was requested.");
                    throw new InvalidOperationException("Unreachable");
                }

                currentRecordBase += currentRecordHeader->RecordLength;
            }
        }

        return new ReadUsnJournalResult(ReadUsnJournalStatus.Success, new Usn(nextUsn), recordsToReturn);
    }

    public static QueryUsnJournalResult TryQueryUsnJournal(SafeFileHandle volumeHandle)
    {
        var data = new QueryUsnJournalData();

        bool ioctlSuccess = DeviceIoControl(
            volumeHandle,
            ioControlCode: NativeIOConstants.FsctlQueryUsnJournal,
            inputBuffer: IntPtr.Zero,
            inputBufferSize: 0,
            outputBuffer: data,
            outputBufferSize: QueryUsnJournalData.Size,
            bytesReturned: out int bytesReturned,
            overlapped: IntPtr.Zero);
        int error = Marshal.GetLastWin32Error();

        if (!ioctlSuccess)
        {
            QueryUsnJournalStatus errorStatus;
            switch ((uint)error)
            {
                case NativeIOConstants.ErrorJournalNotActive:
                    errorStatus = QueryUsnJournalStatus.JournalNotActive;
                    break;
                case NativeIOConstants.ErrorJournalDeleteInProgress:
                    errorStatus = QueryUsnJournalStatus.JournalDeleteInProgress;
                    break;
                case NativeIOConstants.ErrorInvalidFunction:
                case NativeIOConstants.ErrorNotSupported:
                    errorStatus = QueryUsnJournalStatus.VolumeDoesNotSupportChangeJournals;
                    break;
                case NativeIOConstants.ErrorInvalidParameter:
                    errorStatus = QueryUsnJournalStatus.InvalidParameter;
                    break;
                case NativeIOConstants.ErrorAccessDenied:
                    errorStatus = QueryUsnJournalStatus.AccessDenied;
                    break;
                default:
                    throw new Win32Exception(error, "DeviceIoControl(FSCTL_QUERY_USN_JOURNAL)");
            }

            return new QueryUsnJournalResult(errorStatus, data: null);
        }

        return new QueryUsnJournalResult(QueryUsnJournalStatus.Success, data);
    }

    public static unsafe UsnRecord? ReadFileUsnByHandle(SafeFileHandle fileHandle, bool forceJournalVersion2 = false)
    {

        // We support V2 and V3 records. V3 records (with ReFS length FileIds) are larger, so we allocate a buffer on that assumption.
        int recordBufferLength = NativeUsnRecordV3.MaximumSize;
        byte* recordBuffer = stackalloc byte[recordBufferLength];

        ReadFileUsnData readOptions = forceJournalVersion2 ? ReadFileUsnData.NtfsCompatible : ReadFileUsnData.NtfsAndReFSCompatible;

        if (!DeviceIoControl(
                fileHandle,
                ioControlCode: NativeIOConstants.FsctlReadFileUsnData,
                inputBuffer: (IntPtr)(&readOptions),
                inputBufferSize: ReadFileUsnData.Size,
                outputBuffer: (IntPtr)recordBuffer,
                outputBufferSize: recordBufferLength,
                bytesReturned: out int bytesReturned,
                overlapped: IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == NativeIOConstants.ErrorJournalDeleteInProgress ||
                error == NativeIOConstants.ErrorJournalNotActive ||
                error == NativeIOConstants.ErrorInvalidFunction ||
                error == NativeIOConstants.ErrorOnlyIfConnected ||
                error == NativeIOConstants.ErrorAccessDenied ||
                error == NativeIOConstants.ErrorNotSupported)
            {
                return null;
            }

            throw new Win32Exception(error, "DeviceIoControl(FSCTL_READ_FILE_USN_DATA)");
        }

        NativeUsnRecordHeader* recordHeader = (NativeUsnRecordHeader*)recordBuffer;

        Contract.Assume(
            bytesReturned >= NativeUsnRecordHeader.Size,
            "Not enough data returned for a valid USN record header");

        Contract.Assume(
            bytesReturned == recordHeader->RecordLength,
            "RecordLength field disagrees from number of bytes actually returned; but we were expecting exactly one record.");

        UsnRecord resultRecord;
        if (recordHeader->MajorVersion == 3)
        {
            Contract.Assume(!forceJournalVersion2);

            Contract.Assume(
                bytesReturned >= NativeUsnRecordV3.MinimumSize && bytesReturned <= NativeUsnRecordV3.MaximumSize,
                "FSCTL_READ_FILE_USN_DATA returned an amount of data that does not correspond to a valid USN_RECORD_V3.");

            NativeUsnRecordV3* record = (NativeUsnRecordV3*)recordBuffer;

            Contract.Assume(
                record->Reason == 0 && record->TimeStamp == 0 && record->SourceInfo == 0,
                "FSCTL_READ_FILE_USN_DATA scrubs these fields. Marshalling issue?");

            string fileName = string.Empty; //  Encoding.Unicode.GetString(recordBuffer + record->FileNameOffset, record->FileNameLength);

            resultRecord = new UsnRecord(
                record->FileReferenceNumber,
                record->ParentFileReferenceNumber,
                record->Usn,
                (UsnChangeReasons)record->Reason,
                record->TimeStamp,
                fileName);
        }
        else if (recordHeader->MajorVersion == 2)
        {
            Contract.Assume(
                bytesReturned >= NativeUsnRecordV2.MinimumSize && bytesReturned <= NativeUsnRecordV2.MaximumSize,
                "FSCTL_READ_FILE_USN_DATA returned an amount of data that does not correspond to a valid USN_RECORD_V2.");

            NativeUsnRecordV2* record = (NativeUsnRecordV2*)recordBuffer;

            Contract.Assume(
                record->Reason == 0 && record->TimeStamp == 0 && record->SourceInfo == 0,
                "FSCTL_READ_FILE_USN_DATA scrubs these fields. Marshalling issue?");

            string fileName = string.Empty; // Encoding.Unicode.GetString(recordBuffer + record->FileNameOffset, record->FileNameLength);

            resultRecord = new UsnRecord(
                new FileId(0, record->FileReferenceNumber),
                new FileId(0, record->ParentFileReferenceNumber),
                record->Usn,
                (UsnChangeReasons)record->Reason,
                record->TimeStamp,
                fileName);
        }
        else
        {
            Contract.Assume(false, "An unrecognized record version was returned, even though version 2 or 3 was requested.");
            throw new InvalidOperationException("Unreachable");
        }

        return resultRecord;
    }

    public static unsafe Usn? TryWriteUsnCloseRecordByHandle(SafeFileHandle fileHandle)
    {
        ulong writtenUsn;

        if (!DeviceIoControl(
                fileHandle,
                ioControlCode: NativeIOConstants.FsctlWriteUsnCloseRecord,
                inputBuffer: IntPtr.Zero,
                inputBufferSize: 0,
                outputBuffer: (IntPtr)(&writtenUsn),
                outputBufferSize: sizeof(ulong),
                bytesReturned: out int bytesReturned,
                overlapped: IntPtr.Zero))
        {
            int error = Marshal.GetLastWin32Error();

            if (error == NativeIOConstants.ErrorJournalDeleteInProgress ||
                error == NativeIOConstants.ErrorJournalNotActive ||
                error == NativeIOConstants.ErrorWriteProtect)
            {
                return null;
            }

            throw new Win32Exception(error, "DeviceIoControl(FSCTL_WRITE_USN_CLOSE_RECORD)");
        }

        Contract.Assume(bytesReturned == sizeof(ulong));

        return new Usn(writtenUsn);
    }

    public static ulong GetVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
    {
        FileIdAndVolumeId? maybeInfo = TryGetFileIdAndVolumeIdByHandle(fileHandle);
        if (maybeInfo.HasValue)
        {
            return maybeInfo.Value.VolumeSerialNumber;
        }

        return GetShortVolumeSerialNumberByHandle(fileHandle);
    }

    public static unsafe FileIdAndVolumeId? TryGetFileIdAndVolumeIdByHandle(SafeFileHandle fileHandle)
    {
        FileIdAndVolumeId info = default;
        return GetFileInformationByHandleEx(fileHandle, (uint)FileInfoByHandleClass.FileIdInfo, (IntPtr)(&info), FileIdAndVolumeId.Size)
            ? info
            : null;
    }

    public static unsafe uint GetShortVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
    {
        uint serial = 0;
        bool success = GetVolumeInformationByHandleW(
            fileHandle,
            volumeNameBuffer: null,
            volumeNameBufferSize: 0,
            volumeSerial: (IntPtr)(&serial),
            maximumComponentLength: IntPtr.Zero,
            fileSystemFlags: IntPtr.Zero,
            fileSystemNameBuffer: null,
            fileSystemNameBufferSize: 0);
        if (!success)
        {
            int hr = Marshal.GetLastWin32Error();
            throw new Win32Exception(hr, "GetVolumeInformationByHandleW");
        }

        return serial;
    }

    public static string[] GetMountPointsForVolume(string volumeDeviceName)
    {
        var volumeNamePathBuffer = new StringBuilder(capacity: NativeIOConstants.MaxPath + 1);

        GetVolumePathNamesForVolumeNameW(volumeDeviceName, volumeNamePathBuffer, volumeNamePathBuffer.Capacity, out int length);
        if (length == 0)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"GetVolumePathNamesForVolumeNameW({volumeDeviceName})");
        }

        if (length > volumeNamePathBuffer.Capacity)
        {
            volumeNamePathBuffer.Capacity = length;
        }

        volumeNamePathBuffer.Clear();

        if (!GetVolumePathNamesForVolumeNameW(volumeDeviceName, volumeNamePathBuffer, volumeNamePathBuffer.Capacity, out length))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return volumeNamePathBuffer.ToString().Split('\0').ToArray();
    }

    public static OpenFileResult TryOpenDirectory(
        string directoryPath,
        FileDesiredAccess desiredAccess,
        FileShare shareMode,
        FileMode fileMode,
        FileFlagsAndAttributes flagsAndAttributes,
        out SafeFileHandle? handle)
    {
        handle = CreateFileW(
            directoryPath,
            desiredAccess | FileDesiredAccess.Synchronize,
            shareMode,
            lpSecurityAttributes: IntPtr.Zero,
            dwCreationDisposition: fileMode,
            dwFlagsAndAttributes: flagsAndAttributes | FileFlagsAndAttributes.FileFlagBackupSemantics,
            hTemplateFile: IntPtr.Zero);
        int hr = Marshal.GetLastWin32Error();

        if (handle.IsInvalid)
        {
            handle = null;
            return OpenFileResult.Create(directoryPath, hr, fileMode, handleIsValid: false);
        }

        return OpenFileResult.Create(directoryPath, hr, fileMode, handleIsValid: true);
    }

    public static OpenFileResult TryOpenFileById(
            SafeFileHandle existingHandleOnVolume,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle? handle)
    {
        var fileIdDescriptor = new FileIdDescriptor(fileId);
        handle = OpenFileById(
            existingHandleOnVolume,
            fileIdDescriptor,
            desiredAccess,
            shareMode,
            lpSecurityAttributes: IntPtr.Zero,
            dwFlagsAndAttributes: flagsAndAttributes);
        int hr = Marshal.GetLastWin32Error();

        if (handle.IsInvalid)
        {
            handle = null;
            return OpenFileResult.CreateForOpeningById(hr, FileMode.Open, handleIsValid: false);
        }

        return OpenFileResult.CreateForOpeningById(hr, FileMode.Open, handleIsValid: true);
    }

    public static OpenFileResult TryCreateOrOpenFile(
        string path,
        FileDesiredAccess desiredAccess,
        FileShare shareMode,
        FileMode creationDisposition,
        FileFlagsAndAttributes flagsAndAttributes,
        out SafeFileHandle? handle)
    {
        handle = CreateFileW(
            path,
            desiredAccess,
            shareMode,
            lpSecurityAttributes: IntPtr.Zero,
            dwCreationDisposition: creationDisposition,
            dwFlagsAndAttributes: flagsAndAttributes,
            hTemplateFile: IntPtr.Zero);
        int hr = Marshal.GetLastWin32Error();

        if (handle.IsInvalid)
        {
            handle = null;
            return OpenFileResult.Create(path, hr, creationDisposition, handleIsValid: false);
        }

        return OpenFileResult.Create(path, hr, creationDisposition, handleIsValid: true);
    }

    public static bool IsProcessElevated()
    {
        bool ret = false;
        IntPtr hToken;

        if (OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_QUERY, out hToken))
        {
            uint tokenInfLength = 0;

            // first call gets lenght of tokenInformation
            ret = GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, IntPtr.Zero, tokenInfLength, out tokenInfLength);

            IntPtr tokenInformation = Marshal.AllocHGlobal((IntPtr)tokenInfLength);

            if (tokenInformation != IntPtr.Zero)
            {
                ret = GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, tokenInformation, tokenInfLength, out tokenInfLength);

                if (ret)
                {
#pragma warning disable CS8605 // Unboxing a possibly null value.
                    TOKEN_ELEVATION tokenElevation = (TOKEN_ELEVATION)Marshal.PtrToStructure(tokenInformation, typeof(TOKEN_ELEVATION));
#pragma warning restore CS8605 // Unboxing a possibly null value.

                    ret = tokenElevation.TokenIsElevated != 0;
                }

                Marshal.FreeHGlobal(tokenInformation);
                CloseHandle(hToken);
            }
        }

        return ret;
    }

    internal readonly struct ErrorModeContext : IDisposable
    {
        private readonly int _oldErrorMode;

        /// <summary>
        /// Creates an error mode context that represent pushing <paramref name="thisErrorMode"/>.
        /// </summary>
        private ErrorModeContext(int oldErrorMode)
        {
            _oldErrorMode = oldErrorMode;
        }

        /// <summary>
        /// Pushes an error mode context which is the current mode with the given extra flags set.
        /// (i.e., we push <c><see cref="GetThreadErrorMode"/> | <paramref name="additionalFlags"/></c>)
        /// </summary>
        public static ErrorModeContext PushWithAddedFlags(int additionalFlags)
        {
            int currentErrorMode = GetThreadErrorMode();
            int thisErrorMode = currentErrorMode | additionalFlags;

            if (!SetThreadErrorMode(thisErrorMode, out _))
            {
                int hr = Marshal.GetLastWin32Error();
                throw new Win32Exception(hr, "SetThreadErrorMode");
            }

            return new ErrorModeContext(oldErrorMode: currentErrorMode);
        }

        /// <summary>
        /// Sets <c>SEM_FAILCRITICALERRORS</c> in the thread's error mode (if it is not set already).
        /// The returned <see cref="ErrorModeContext"/> must be disposed to restore the prior error mode (and the disposal must occur on the same thread).
        /// </summary>
        /// <remarks>
        /// The intended effect is to avoid a blocking message box if a file path on a CD / floppy drive letter is poked without media inserted.
        /// This is neccessary before using volume management functions such as <see cref="ListVolumeGuidPathsAndSerials"/>
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/ms680621(v=vs.85).aspx
        /// </remarks>
        public static ErrorModeContext DisableMessageBoxForRemovableMedia() => PushWithAddedFlags(1 /* SEM_FAILCRITICALERRORS */);

        /// <summary>
        /// Pops this error mode context off of the thread's error mode stack.
        /// </summary>
        public void Dispose()
        {
            if (!SetThreadErrorMode(_oldErrorMode, out _))
            {
                int hr = Marshal.GetLastWin32Error();
                throw new Win32Exception(hr, "SetThreadErrorMode");
            }
        }
    }
}
