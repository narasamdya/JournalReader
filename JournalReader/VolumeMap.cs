using Microsoft.Win32.SafeHandles;

namespace JournalReader;

/// <summary>
/// Map local volumes, allowing lookup of volume path by serial number and opening files by ID.
/// </summary>
/// <remarks>
/// X:\ marks the spot.
/// </remarks>
internal sealed class VolumeMap
{
    private readonly Dictionary<ulong, VolumeGuidPath> m_volumePathsBySerial = new();

    private VolumeMap()
    {
    }

    /// <summary>
    /// Creates a map of local volumes. In the event of a collision which prevents constructing a serial -> path mapping,
    /// a warning is logged and those volumes are excluded from the map. On failure, returns null.
    /// </summary>
    public static VolumeMap CreateMapOfAllLocalVolumes()
    {
        var map = new VolumeMap();
        List<(VolumeGuidPath volumeGuidPath, ulong serial)> volumes = Native.ListVolumeGuidPathsAndSerials();

        foreach (var volume in volumes)
        {
            VolumeGuidPath collidedGuidPath;
            if (map.m_volumePathsBySerial.TryGetValue(volume.serial, out collidedGuidPath))
            {
                if (collidedGuidPath.IsValid)
                {
                    // Poison this entry so that we know it is unusable on lookup (ambiguous)
                    map.m_volumePathsBySerial[volume.serial] = VolumeGuidPath.Invalid;
                }
            }
            else
            {
                map.m_volumePathsBySerial.Add(volume.serial, volume.volumeGuidPath);
            }
        }

        return map;
    }

    /// <summary>
    /// Gets all (volume serial, volume guid path) pairs in this map.
    /// </summary>
    public IEnumerable<KeyValuePair<ulong, VolumeGuidPath>> Volumes => m_volumePathsBySerial.Where(kvp => kvp.Value.IsValid);

    /// <summary>
    /// Looks up the GUID path that corresponds to the given serial. Returns <see cref="VolumeGuidPath.Invalid"/> if there is no volume with the given
    /// serial locally.
    /// </summary>
    /// <remarks>
    /// The serial should have 64 significant bits when available on Windows 8.1+ (i.e., the serial returned by <c>GetVolumeInformation</c>
    /// is insufficient). The appropriate serial can be retrieved from any handle on the volume via <see cref="FileUtilities.GetVolumeSerialNumberByHandle"/>.
    /// </remarks>
    public bool TryGetVolumePathBySerial(ulong volumeSerial, out VolumeGuidPath volumeGuidPath) => m_volumePathsBySerial.TryGetValue(volumeSerial, out volumeGuidPath);

    /// <summary>
    /// Looks up the GUID path for the volume containing the given file handle. Returns <see cref="VolumeGuidPath.Invalid"/> if a matching volume cannot be found
    /// (though that suggests that this volume map is incomplete).
    /// </summary>
    public bool TryGetVolumePathForHandle(SafeFileHandle handle, out VolumeGuidPath volumeGuidPath) => TryGetVolumePathBySerial(Native.GetVolumeSerialNumberByHandle(handle), out volumeGuidPath);

    /// <summary>
    /// Creates a <see cref="FileAccessor"/> which can open files based on this volume map.
    /// </summary>
    public FileAccessor CreateFileAccessor() => new(this);

    /// <summary>
    /// Creates a <see cref="VolumeAccessor"/> which can open volume handles based on this volume map.
    /// </summary>
    public VolumeAccessor CreateVolumeAccessor() => new(this);
}

/// <summary>
/// Allows opening a batch of files based on their <see cref="FileId"/> and volume serial number.
/// </summary>
/// <remarks>
/// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
/// This class is disposable since it holds handles to volume root paths. At most, it holds one handle to each volume.
/// </remarks>
internal sealed class FileAccessor : IDisposable
{
    private readonly Dictionary<ulong, SafeFileHandle> _volumeRootHandles = new();
    private readonly VolumeMap _map;

    internal FileAccessor(VolumeMap map)
    {
        _map = map;
        Disposed = false;
    }

    /// <summary>
    /// Error reasons for <see cref="FileAccessor.TryOpenFileById"/>
    /// </summary>
    public enum OpenFileByIdResult : byte
    {
        /// <summary>
        /// Opened a handle.
        /// </summary>
        Succeeded = 0,

        /// <summary>
        /// The containing volume could not be opened.
        /// </summary>
        FailedToOpenVolume = 1,

        /// <summary>
        /// The given file ID does not exist on the volume.
        /// </summary>
        FailedToFindFile = 2,

        /// <summary>
        /// The file ID exists on the volume but could not be opened
        /// (due to permissions, a sharing violation, a pending deletion, etc.)
        /// </summary>
        FailedToAccessExistentFile = 3,
    }

    /// <summary>
    /// Tries to open a handle to the given file as identified by a (<paramref name="volumeSerial"/>, <paramref name="fileId"/>) pair.
    /// If the result is <see cref="OpenFileByIdResult.Succeeded"/>, <paramref name="fileHandle"/> has been set to a valid handle.
    /// </summary>
    /// <remarks>
    /// This method is not thread safe (see <see cref="FileAccessor"/> remarks).
    /// </remarks>
    public OpenFileByIdResult TryOpenFileById(
        ulong volumeSerial,
        FileId fileId,
        FileDesiredAccess desiredAccess,
        FileShare shareMode,
        FileFlagsAndAttributes flagsAndAttributes,
        out SafeFileHandle? fileHandle)
    {
        SafeFileHandle? volumeRootHandle = GetVolumeRootOrNull(volumeSerial);

        if (volumeRootHandle == null)
        {
            fileHandle = null;
            return OpenFileByIdResult.FailedToOpenVolume;
        }

        OpenFileResult openResult = Native.TryOpenFileById(
            volumeRootHandle,
            fileId,
            desiredAccess,
            shareMode,
            flagsAndAttributes,
            out fileHandle);

        return !openResult.Succeeded
            ? (openResult.Status.IsNonexistent() ? OpenFileByIdResult.FailedToFindFile : OpenFileByIdResult.FailedToAccessExistentFile)
            : OpenFileByIdResult.Succeeded;
    }

    /// <summary>
    /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Needs to be public for contract preconditions.
    /// </remarks>
    public bool Disposed { get; private set; }

    /// <summary>
    /// Closes any handles held by this instance.
    /// </summary>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        foreach (SafeFileHandle handle in _volumeRootHandles.Values)
        {
            handle.Dispose();
        }

        Disposed = true;
    }

    /// <summary>
    /// Creates a volume root handle or retrieves an existing one.
    /// </summary>
    /// <remarks>
    /// This is the un-synchronized get-or-add operation resulting in <see cref="FileAccessor"/>
    /// not being thread-safe.
    /// </remarks>
    private SafeFileHandle? GetVolumeRootOrNull(ulong volumeSerial)
    {
        if (!_volumeRootHandles.TryGetValue(volumeSerial, out SafeFileHandle? volumeRootHandle))
        {
            if (!_map.TryGetVolumePathBySerial(volumeSerial, out VolumeGuidPath volumeRootPath) || !volumeRootPath.IsValid)
            {
                return null;
            }

            if (Native.TryOpenDirectory(
                volumeRootPath.Path,
                FileDesiredAccess.None,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out volumeRootHandle).Succeeded)
            {
                return null;
            }

            _volumeRootHandles.Add(volumeSerial, volumeRootHandle!);
        }

        return volumeRootHandle;
    }
}

/// <summary>
/// Allows opening a batch of volumes (devices) based on their volume serial numbers.
/// </summary>
/// <remarks>
/// Unlike the <see cref="VolumeMap"/> upon which it operates, this class is not thread-safe.
/// This class is disposable since it holds volume handles. At most, it holds one handle to each volume.
/// Accessing volume handles is a privileged operation; attempting to open volume handles will likely fail if not elevated.
/// </remarks>
public sealed class VolumeAccessor : IDisposable
{
    private readonly Dictionary<ulong, SafeFileHandle> _volumeHandles = new();
    private readonly VolumeMap _map;

    internal VolumeAccessor(VolumeMap map)
    {
        _map = map;
        Disposed = false;
    }

    /// <summary>
    /// Creates a volume root handle or retrieves an existing one.
    /// </summary>
    /// <remarks>
    /// The returned handle should not be disposed.
    /// </remarks>
    public SafeFileHandle? GetVolumeHandleOrNull(SafeFileHandle handleOnPathInVolume) => GetVolumeHandleOrNull(Native.GetVolumeSerialNumberByHandle(handleOnPathInVolume));

    /// <summary>
    /// Creates a volume root handle or retrieves an existing one.
    /// </summary>
    /// <remarks>
    /// This is the un-synchronized get-or-add operation resulting in <see cref="VolumeAccessor"/>
    /// not being thread-safe.
    /// The returned handle should not be disposed.
    /// </remarks>
    private SafeFileHandle? GetVolumeHandleOrNull(ulong volumeSerial)
    {
        if (!_volumeHandles.TryGetValue(volumeSerial, out SafeFileHandle? volumeHandle))
        {
            if (!_map.TryGetVolumePathBySerial(volumeSerial, out VolumeGuidPath volumeRootPath) || !volumeRootPath.IsValid)
            {
                return null;
            }

            OpenFileResult openResult = Native.TryCreateOrOpenFile(
                volumeRootPath.GetDevicePath(),
                FileDesiredAccess.GenericRead,
                FileShare.ReadWrite | FileShare.Delete,
                FileMode.Open,
                FileFlagsAndAttributes.None,
                out volumeHandle);

            if (!openResult.Succeeded)
            {
                return null;
            }

            _volumeHandles.Add(volumeSerial, volumeHandle!);
        }

        return volumeHandle;
    }

    /// <summary>
    /// Indicates if this instance has been disposed via <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Needs to be public for contract preconditions.
    /// </remarks>
    public bool Disposed { get; private set; }

    /// <summary>
    /// Closes any handles held by this instance.
    /// </summary>
    public void Dispose()
    {
        if (Disposed)
        {
            return;
        }

        foreach (SafeFileHandle handle in _volumeHandles.Values)
        {
            handle.Dispose();
        }

        Disposed = true;
    }
}
