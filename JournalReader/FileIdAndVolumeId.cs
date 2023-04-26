﻿using System.Runtime.InteropServices;

namespace JournalReader;

/// <summary>
/// This corresponds to FILE_ID_INFO as returned by GetFileInformationByHandleEx (with <see cref="BuildXL.Native.IO.Windows.FileSystemWin.FileInfoByHandleClass.FileIdInfo"/>).
/// http://msdn.microsoft.com/en-us/library/windows/desktop/hh802691(v=vs.85).aspx
/// </summary>
/// <remarks>
/// Note that the FileId field supports a ReFS-sized ID. This is because the corresponding FileIdInfo class was added in 8.1 / Server 2012 R2.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct FileIdAndVolumeId : IEquatable<FileIdAndVolumeId>
{
    internal static readonly int Size = Marshal.SizeOf<FileIdAndVolumeId>();

    /// <summary>
    /// Volume containing the file.
    /// </summary>
    public readonly ulong VolumeSerialNumber;

    /// <summary>
    /// Unique identifier of the referenced file (within the containing volume).
    /// </summary>
    public readonly FileId FileId;

    /// <obvious />
    public FileIdAndVolumeId(ulong volumeSerialNumber, FileId fileId)
    {
        VolumeSerialNumber = volumeSerialNumber;
        FileId = fileId;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FileIdAndVolumeId other && Equals(other);

    /// <inheritdoc />
    public bool Equals(FileIdAndVolumeId other) => FileId == other.FileId && VolumeSerialNumber == other.VolumeSerialNumber;

    /// <inheritdoc />
    public override int GetHashCode() => (FileId.GetHashCode(), VolumeSerialNumber.GetHashCode()).GetHashCode();

    /// <inheritdoc />
    public static bool operator ==(FileIdAndVolumeId left, FileIdAndVolumeId right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(FileIdAndVolumeId left, FileIdAndVolumeId right) => !left.Equals(right);

    /// <summary>
    /// Serializes this instance of <see cref="FileIdAndVolumeId"/>.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(VolumeSerialNumber);
        FileId.Serialize(writer);
    }

    /// <summary>
    /// Deserializes into an instance of <see cref="FileIdAndVolumeId"/>.
    /// </summary>
    public static FileIdAndVolumeId Deserialize(BinaryReader reader) => new FileIdAndVolumeId(reader.ReadUInt64(), FileId.Deserialize(reader));
}
