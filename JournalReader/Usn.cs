namespace JournalReader;

internal readonly struct Usn : IEquatable<Usn>, IComparable<Usn>
{
    /// <summary>
    /// Version value.
    /// </summary>
    /// <remarks>
    /// For NTFS change journal, this value represents journal offsets, which are totally ordered within a volume.
    /// </remarks>
    public readonly ulong Value;

    /// <summary>
    /// Zero USN.
    /// </summary>
    public static readonly Usn Zero = new(0);

    /// <nodoc />
    public Usn(ulong value)
    {
        Value = value;
    }

    /// <summary>
    /// Indicates if this is the lowest representable USN (0) == <c>default(Usn)</c>.
    /// </summary>
    /// <remarks>
    /// For NTFS change journal, the zero USN is special in that all files claim that USN if the volume's journal is disabled
    /// (or if they have not been modified since the journal being enabled).
    /// </remarks>
    public bool IsZero => Value == 0;

    /// <nodoc />
    public bool Equals(Usn other) => Value == other.Value;

    /// <nodoc />
    public override bool Equals(object? obj) => obj is Usn other && Equals(other);

    /// <nodoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <nodoc />
    public static bool operator ==(Usn left, Usn right) => left.Equals(right);

    /// <nodoc />
    public static bool operator !=(Usn left, Usn right) => !left.Equals(right);

    /// <nodoc />
    public static bool operator <(Usn left, Usn right) => left.Value < right.Value;

    /// <nodoc />
    public static bool operator >(Usn left, Usn right) => left.Value > right.Value;

    /// <nodoc />
    public static bool operator <=(Usn left, Usn right) => left.Value <= right.Value;

    /// <nodoc />
    public static bool operator >=(Usn left, Usn right) => left.Value >= right.Value;

    /// <nodoc />
    public int CompareTo(Usn other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => $"{{ USN {Value:x} }}";
}
