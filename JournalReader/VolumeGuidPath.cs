namespace JournalReader;

/// <summary>
/// Represents a <c>\\?\Volume{GUID}\</c> style path to the root of a volume.
/// </summary>
/// <remarks>
/// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365248(v=vs.85).aspx
/// </remarks>
internal readonly struct VolumeGuidPath : IEquatable<VolumeGuidPath>
{
    /// <summary>
    /// Invalid guid path (compares equal to all other invalid guid paths).
    /// </summary>
    public static readonly VolumeGuidPath Invalid = default;

    private readonly string _path;

    private VolumeGuidPath(string path)
    {
        if (!IsValidVolumeGuidPath(path))
        {
            throw new ArgumentException("Invalid volume GUID path: " + path);
        }

        _path = path;
    }

    /// <summary>
    /// Indicates if this instance is valid (note that <c>default(VolumeGuidPath)</c> is not).
    /// </summary>
    public bool IsValid => _path != null;

    /// <summary>
    /// Returns the string representation of the volume GUID path (ends in a trailing slash).
    /// </summary>
    public string Path => _path;

    /// <summary>
    /// Returns the string representation of the volume GUID device path (like <see cref="Path" /> but without a trailing
    /// slash).
    /// </summary>
    public string GetDevicePath()
    {
        if (_path[^1] != '\\')
        {
            throw new InvalidDataException("Volume GUID path does not end in a trailing slash: " + _path);
        }

        return _path[..^1];
    }

    /// <summary>
    /// Attempts to parse a string path as a volume guid path.
    /// </summary>
    public static bool TryCreate(string path, out VolumeGuidPath parsed)
    {
        if (!IsValidVolumeGuidPath(path))
        {
            parsed = Invalid;
            return false;
        }

        parsed = new VolumeGuidPath(path);
        return true;
    }

    /// <summary>
    /// Parses a string path as a volume guid path. The string path must be valid.
    /// </summary>
    public static VolumeGuidPath Create(string path) => TryCreate(path, out VolumeGuidPath result) ? result : Invalid;

    /// <summary>
    /// Validates that the given string is a volume guid PATH.
    /// </summary>
    public static bool IsValidVolumeGuidPath(string path)
    {
        const string VolumePrefix = @"\\?\Volume{";

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (!path.StartsWith(VolumePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The last character should be a backslash (volume root directory), and it should be the only such slash after the prefix.
        if (path.IndexOf('\\', VolumePrefix.Length) != path.Length - 1)
        {
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override string ToString() => _path;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is VolumeGuidPath other && Equals(other);

    /// <inheritdoc />
    public bool Equals(VolumeGuidPath other) =>
        ReferenceEquals(_path, other._path)
        || (_path != null && _path.Equals(other._path, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public override int GetHashCode() => _path?.GetHashCode() ?? 0;

    /// <nodoc />
    public static bool operator ==(VolumeGuidPath left, VolumeGuidPath right) => left.Equals(right);

    /// <nodoc />
    public static bool operator !=(VolumeGuidPath left, VolumeGuidPath right) => !left.Equals(right);
}
