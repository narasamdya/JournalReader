﻿using System.Runtime.InteropServices;

namespace JournalReader;

/// <summary>
/// 128-bit file ID, which durably and uniquely represents a file on an NTFS or ReFS volume.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly struct FileId : IEquatable<FileId>
{
    /// <summary>
    /// Low bits
    /// </summary>
    public readonly ulong Low;

    /// <summary>
    /// High bits
    /// </summary>
    public readonly ulong High;

    /// <summary>
    /// Constructs a file ID from two longs, constituting the high and low bits (128 bits total).
    /// </summary>
    public FileId(ulong high, ulong low)
    {
        High = high;
        Low = low;
    }

    /// <inheritdoc />
    public override string ToString() => $"[FileID 0x{High:X16}{Low:X16}]";

    /// <inheritdoc />
    public bool Equals(FileId other) => other.High == High && other.Low == Low;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is FileId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // An ancient prophecy has foretold of a ReFS file ID that actually needed the high bits.
        return unchecked((int)((High ^ Low) ^ ((High ^ Low) >> 32)));
    }

    /// <inheritdoc />

    public static bool operator ==(FileId left, FileId right) => left.Equals(right);

    /// <inheritdoc />
    public static bool operator !=(FileId left, FileId right) => !left.Equals(right);

    /// <summary>
    /// Serializes this instance of <see cref="FileId"/>.
    /// </summary>
    public void Serialize(BinaryWriter writer)
    {
        writer.Write(High);
        writer.Write(Low);
    }

    /// <summary>
    /// Deserializes into an instance of <see cref="FileId"/>.
    /// </summary>
    public static FileId Deserialize(BinaryReader reader) => new FileId(reader.ReadUInt64(), reader.ReadUInt64());
}
