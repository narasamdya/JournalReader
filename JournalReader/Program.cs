using static JournalReader.JournalAccessor;

namespace JournalReader;

public class JournalReader
{
    public static int Main(string[] args)
    {
        string cmd = args[0];

        int exitCode = cmd.ToUpperInvariant() switch
        {
            "IDENTITY" => GetVersionedFileIdentity(Path.GetFullPath(args[1])),
            "QUERY"    => QueryUsnJournal(args.Length >= 2 ? args[1] : null),
            "READ"     => ReadUsnJournal(args[1], args.Length >= 3 && args[2].ToUpperInvariant() == "WAIT"),
            "VOLUMES"  => GetVolumes(),
            _ => throw new ArgumentException($"Unknown command: {cmd}"),
        }; ;

        return exitCode;
    }

    private static int GetVolumes()
    {
        Possible<VolumeMap> maybeVolumeMap = VolumeMap.CreateMapOfAllLocalVolumes();
        if (!maybeVolumeMap.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get volume map: {maybeVolumeMap.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        VolumeMap volumeMap = maybeVolumeMap.Result;

        Console.WriteLine($"Volume map:");
        foreach (var volumeGuidPath in volumeMap.Volumes)
        {
            Possible<string[]> maybePathNames = Native.GetMountPointsForVolumeOrFail(volumeGuidPath.Value);

            if (!maybePathNames.Succeeded)
            {
                Console.Error.WriteLine($"  {volumeGuidPath.Value} ({volumeGuidPath.Key}): {volumeGuidPath.Key} -- <error: {maybePathNames.Failure.DescribeIncludingInnerFailures()}>");
                continue;
            }

            string[] pathNames = maybePathNames.Result;
            string pathNamesString = pathNames.Length == 0 ? "<none>" : string.Join(", ", pathNames) + $" (count: {pathNames.Length})";
            Console.WriteLine($"  {volumeGuidPath.Value} ({volumeGuidPath.Key}): {volumeGuidPath.Key} -- {pathNamesString}");
        }

        return 0;
    }

    private static int GetVersionedFileIdentity(string path)
    {
        Possible<(FileIdAndVolumeId fileIdAndVolumeId, UsnRecord usnRecord)> maybeResult = GetVersionedFileIdentityByHandle(path);

        if (!maybeResult.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get versioned file identity for '{path}': {maybeResult.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        (FileIdAndVolumeId fileIdAndVolumeId, UsnRecord usnRecord) result = maybeResult.Result;

        Console.WriteLine($"Versioned file identity for '{path}':");
        Console.WriteLine($"  File ID              : {result.fileIdAndVolumeId.FileId}");
        Console.WriteLine($"  Volume serial number : {result.fileIdAndVolumeId.VolumeSerialNumber}");
        Console.WriteLine($"  File name            : {result.usnRecord.FileName}");
        Console.WriteLine($"  USN                  : {result.usnRecord.Usn}");
        Console.WriteLine($"  Time stamp           : {DateTime.FromFileTimeUtc(result.usnRecord.Timestamp)}");

        return 0;
    }

    private static int ReadUsnJournal(string mountPoint, bool wait)
    {
        Possible<VolumeMap> maybeVolumeMap = VolumeMap.CreateMapOfAllLocalVolumes();

        if (!maybeVolumeMap.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get volume map: {maybeVolumeMap.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        VolumeMap volumeMap = maybeVolumeMap.Result;
        Possible<JournalAccessor> maybeAccessor = GetJournalAccessor(volumeMap, Path.GetTempFileName());

        if (!maybeAccessor.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get journal accessor: {maybeAccessor.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        JournalAccessor journalAccessor = maybeAccessor.Result;

        Possible<(VolumeGuidPath volumePath, ulong serial)> maybeVolumeGuidPath = Native.GetVolumeGuidPathAndSerialOrFail(mountPoint);
        if (!maybeVolumeGuidPath.Succeeded)
        {
            Console.Error.WriteLine($"Failed getting volume GUID path for '{mountPoint}': {maybeVolumeGuidPath.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        VolumeGuidPath volumeGuidPath = maybeVolumeGuidPath.Result.volumePath;

        MaybeResponse<QueryUsnJournalResult> response = journalAccessor.QueryJournal(new QueryJournalRequest(volumeGuidPath));
        if (response.IsError)
        {
            Console.Error.WriteLine($"Failed querying journal for path '{volumeGuidPath}' ({mountPoint}): {response.Error.Message} ({response.Error.Status})");
            return -1;
        }

        QueryUsnJournalResult result = response.Response;

        journalAccessor.ReadJournal(
            new ReadJournalRequest(volumeGuidPath, result.Data!.UsnJournalId, Usn.Zero, result.Data!.NextUsn),
            (UsnRecord record) =>
            {
                Console.WriteLine($"File ID    : {record.FileId}");
                Console.WriteLine($"File name  : {record.FileName}");
                Console.WriteLine($"USN        : {record.Usn}");
                Console.WriteLine($"Change     : {record.Reason}");
                Console.WriteLine($"Time:      : {DateTime.FromFileTimeUtc(record.Timestamp)}");
                Console.WriteLine();
                if (wait)
                {
                    Console.WriteLine("Press any key to continue, or Ctrl-C to exit...");
                    Console.ReadKey();
                }
            });

        return 0;
    }

    private static int QueryUsnJournal(string? mountPoint)
    {
        Possible<VolumeMap> maybeVolumeMap = VolumeMap.CreateMapOfAllLocalVolumes();

        if (!maybeVolumeMap.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get volume map: {maybeVolumeMap.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        VolumeMap volumeMap = maybeVolumeMap.Result;
        Possible<JournalAccessor> maybeAccessor = GetJournalAccessor(volumeMap, Path.GetTempFileName());

        if (!maybeAccessor.Succeeded)
        {
            Console.Error.WriteLine($"Failed to get journal accessor: {maybeAccessor.Failure.DescribeIncludingInnerFailures()}");
            return -1;
        }

        JournalAccessor journalAccessor = maybeAccessor.Result;
        VolumeGuidPath? volumeGuidPath = null;

        if (!string.IsNullOrEmpty(mountPoint))
        {
            Possible<(VolumeGuidPath volumePath, ulong serial)> maybeVolumeGuidPath = Native.GetVolumeGuidPathAndSerialOrFail(mountPoint);
            if (!maybeVolumeGuidPath.Succeeded)
            {
                Console.Error.WriteLine($"Failed getting volume GUID path for '{mountPoint}': {maybeVolumeGuidPath.Failure.DescribeIncludingInnerFailures()}");
                return -1;
            }

            volumeGuidPath = maybeVolumeGuidPath.Result.volumePath;
        }

        IEnumerable<VolumeGuidPath> volumes = volumeGuidPath == null ? volumeMap.Volumes.Select(kvp => kvp.Value) : new[] { volumeGuidPath.Value };

        if (volumeGuidPath == null)
        {
            Console.WriteLine($"Querying journal for all volumes.");
        }
        else
        {
            Console.WriteLine($"Querying journal for volume '{volumeGuidPath.Value}'.");
        }

        foreach (var volume in volumes)
        {
            string[] pathNames = Native.GetMountPointsForVolume(volume.ToString());
            string pathNamesString = pathNames.Length == 0 ? "<none>" : string.Join(", ", pathNames.Select(p => $"'{p}'"));

            MaybeResponse<QueryUsnJournalResult> response = journalAccessor.QueryJournal(new QueryJournalRequest(volume));
            if (response.IsError)
            {
                Console.Error.WriteLine($"Failed querying journal for path '{volume}' ({pathNamesString}): {response.Error.Message} ({response.Error.Status})");
                continue;
            }

            Console.WriteLine($"Journal for volume '{volume}' ({pathNamesString}):");
            QueryUsnJournalResult result = response.Response;
            Console.WriteLine($"  Status: {result.Status}");
            if (result.Succeeded)
            {
                Console.WriteLine($"  Id          : {result.Data!.UsnJournalId}");
                Console.WriteLine($"  First USN   : {result.Data!.FirstUsn}");
                Console.WriteLine($"  Next USN    : {result.Data!.NextUsn}");
                Console.WriteLine($"  Lowest USN  : {result.Data!.LowestValidUsn}");
                Console.WriteLine($"  Max USN     : {result.Data!.MaxUsn}");
                Console.WriteLine($"  Max size    : {result.Data!.MaximumSize}");
                Console.WriteLine($"  Alloc delta : {result.Data!.AllocationDelta}");
            }

            Console.WriteLine();
            Console.WriteLine();
        }

        return 0;
    }
}