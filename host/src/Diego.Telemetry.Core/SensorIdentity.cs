// Turns a flattened hardware list into unique published ids.
// LibreHardwareMonitorLib 0.9.6 does not always do that itself.
namespace Diego.Telemetry;

/// <summary>
/// Gives every collected sensor a published id that can be a dictionary key.
///
/// LibreHardwareMonitor builds an id as
/// <c>{hardwareId}/{sensorType}/{index}</c>. <c>NvidiaGpu</c> in 0.9.6 always
/// constructs the load sensor named "GPU Memory" at index 3. It also constructs
/// one load sensor per present NVAPI utilization domain, using the domain
/// ordinal as the index. Domain 3 is named "GPU Bus". On an RTX 2080 SUPER both
/// objects are activated during <c>NvidiaGpu</c> construction, so the flattened
/// list contains two rows whose id is <c>/gpu-nvidia/0/load/3</c>.
/// <c>Hardware.Update</c> writes <c>Value</c> on those existing objects. It does
/// not remove either of them, and this host's collect walk does not append a
/// second copy on later polls. The crash is the later <c>ToDictionary</c> on
/// that id, in <see cref="SnapshotBuilder"/>.
///
/// A published id is chosen from the sensor's own name and the known owner of
/// that library slot. List order is not a value. Two rows that cannot be told
/// apart are withheld: status error, code <c>ambiguous_sensor_identity</c>,
/// value null. Their <c>unresolved-N</c> suffix exists so the JSON document
/// still has unique ids. It is not a selection of the first or last reading.
///
/// The one compatibility rule is the slot LibreHardwareMonitor itself reserved.
/// When exactly one row in the colliding group is named "GPU Memory", type
/// Load, index 3, on <c>/gpu-nvidia/{n}</c>, that row keeps the bare library
/// id. A card that never grew the extra row already uses that id for GPU
/// Memory, and upstream commit 035a878 later reserved index 3 for the same
/// sensor. Every other distinct name in the group gets
/// <c>{libraryId}/{name-slug}</c>. Units stay on the sensor type the library
/// reported. A Load row stays percent, including power-topology rows that
/// 0.9.6 mis-types as Load. This host does not relabel them as watts.
///
/// Grouping is the full library id, so <c>/gpu-nvidia/0/load/3</c> and
/// <c>/gpu-nvidia/1/load/3</c> are different GPUs. This is not a PawnIO fault.
/// <c>check-backend</c> only reads the uninstall <c>DisplayVersion</c>. Nothing
/// here asks for a driver change.
/// </summary>
public static class SensorIdentity
{
    public const string ReservedGpuMemoryName = "GPU Memory";

    public static IdentityResolution Resolve(IReadOnlyList<SensorSample> sensors)
    {
        var groups = new List<List<int>>();
        var groupOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sensors.Count; index++)
        {
            var libraryId = sensors[index].Id ?? "";
            if (!groupOf.TryGetValue(libraryId, out var groupIndex))
            {
                groupIndex = groups.Count;
                groupOf[libraryId] = groupIndex;
                groups.Add([]);
            }

            groups[groupIndex].Add(index);
        }

        var published = new SensorSample?[sensors.Count];
        var used = new HashSet<string>(StringComparer.Ordinal);
        var notes = new List<SensorIdentityNote>();

        foreach (var members in groups)
        {
            if (members.Count == 1 && !string.IsNullOrEmpty(sensors[members[0]].Id))
            {
                var sample = sensors[members[0]];
                used.Add(sample.Id);
                published[members[0]] = sample;
            }
        }

        foreach (var members in groups)
        {
            var libraryId = sensors[members[0]].Id ?? "";
            if (members.Count == 1 && !string.IsNullOrEmpty(libraryId))
            {
                continue;
            }

            if (members.Count == 1)
            {
                var sample = sensors[members[0]];
                var id = NextFree(used, "/unidentified", "missing-id");
                var withheld = Withhold(sample, id, "missing_library_id",
                    "A sensor was collected with an empty library id. " +
                    $"It is listed as {id} with status error. Its value was not published, because an empty id cannot be mapped.");
                published[members[0]] = withheld;
                notes.Add(Note("missing-id", "", false, withheld.Error!.Message, [Member(withheld, false)]));
                continue;
            }

            var consumed = new bool[members.Count];
            var bareAssigned = false;
            var memoryOrdinal = ReservedMemoryOrdinal(sensors, members);
            if (memoryOrdinal is int reserved)
            {
                var sample = sensors[members[reserved]];
                if (used.Add(libraryId))
                {
                    published[members[reserved]] = sample;
                    consumed[reserved] = true;
                    bareAssigned = true;
                }
            }

            var nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ordinal in IndexRange(members.Count))
            {
                if (consumed[ordinal])
                {
                    continue;
                }

                var name = sensors[members[ordinal]].Name ?? "";
                nameCount[name] = nameCount.TryGetValue(name, out var count) ? count + 1 : 1;
            }

            var slugAt = new Dictionary<int, string>();
            var slugCount = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var ordinal in IndexRange(members.Count))
            {
                if (consumed[ordinal])
                {
                    continue;
                }

                var name = sensors[members[ordinal]].Name ?? "";
                if (name.Length == 0 || nameCount[name] != 1)
                {
                    continue;
                }

                var slug = Slug(name);
                if (slug.Length == 0)
                {
                    continue;
                }

                slugAt[ordinal] = slug;
                slugCount[slug] = slugCount.TryGetValue(slug, out var count) ? count + 1 : 1;
            }

            foreach (var pair in slugAt)
            {
                if (slugCount[pair.Value] != 1)
                {
                    continue;
                }

                var desired = Combine(libraryId, pair.Value);
                if (!used.Add(desired))
                {
                    continue;
                }

                published[members[pair.Key]] = Retarget(sensors[members[pair.Key]], desired, libraryId);
                consumed[pair.Key] = true;
            }

            var withheldMembers = new List<SensorIdentityMember>();
            var publishedMembers = new List<SensorIdentityMember>();
            foreach (var ordinal in IndexRange(members.Count))
            {
                var sourceIndex = members[ordinal];
                if (!consumed[ordinal])
                {
                    var id = NextFree(used, string.IsNullOrEmpty(libraryId) ? "/unidentified" : libraryId, "unresolved");
                    var message =
                        $"Library id {DisplayId(libraryId)} was shared by sensors that cannot be told apart. " +
                        $"{id} keeps the row visible and publishes no value. " +
                        "The unresolved suffix is not the first or last reading.";
                    var withheld = Withhold(sensors[sourceIndex], id, "ambiguous_sensor_identity", message);
                    published[sourceIndex] = withheld;
                    withheldMembers.Add(Member(withheld, false));
                }
                else
                {
                    var row = published[sourceIndex] ?? throw new InvalidOperationException("A resolved sensor row was missing.");
                    publishedMembers.Add(Member(row, true));
                }
            }

            var resolution = bareAssigned
                ? "reserved-owner"
                : withheldMembers.Count == 0 ? "distinct-names" : "ambiguous";
            var allMembers = publishedMembers.Concat(withheldMembers).ToArray();
            notes.Add(Note(resolution, libraryId, bareAssigned, Describe(libraryId, resolution, bareAssigned, allMembers), allMembers));
        }

        var rows = new SensorSample[sensors.Count];
        for (var index = 0; index < published.Length; index++)
        {
            rows[index] = published[index] ?? throw new InvalidOperationException("Sensor identity resolution left a row unpublished.");
        }

        return new IdentityResolution(rows, notes);
    }

    /// <summary>
    /// Index of the single "GPU Memory" load that owns library index 3 on one NVIDIA GPU.
    /// Null when that owner is missing or repeated. Repeated owners are not guessed.
    /// </summary>
    private static int? ReservedMemoryOrdinal(IReadOnlyList<SensorSample> sensors, List<int> members)
    {
        var found = -1;
        for (var ordinal = 0; ordinal < members.Count; ordinal++)
        {
            if (!IsReservedGpuMemory(sensors[members[ordinal]]))
            {
                continue;
            }

            if (found >= 0)
            {
                return null;
            }

            found = ordinal;
        }

        return found >= 0 ? found : null;
    }

    private static bool IsReservedGpuMemory(SensorSample sample)
    {
        if (!string.Equals(sample.Name, ReservedGpuMemoryName, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(sample.SensorType, "Load", StringComparison.Ordinal) || sample.Index != 3)
        {
            return false;
        }

        var id = sample.Id ?? "";
        const string prefix = "/gpu-nvidia/";
        const string suffix = "/load/3";
        if (!id.StartsWith(prefix, StringComparison.Ordinal) || !id.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        var adapter = id[prefix.Length..^suffix.Length];
        return adapter.Length > 0 && adapter.All(static character => character is >= '0' and <= '9');
    }

    private static string Describe(
        string libraryId,
        string resolution,
        bool bareAssigned,
        IReadOnlyList<SensorIdentityMember> members)
    {
        var shown = string.Join("; ", members.Select(member =>
            $"{DisplayName(member.Name)} -> {member.PublishedId} ({member.NormalizedUnit}, library {(member.LibraryValue?.ToString() ?? "null")}, published {member.ValuePublished})"));
        if (resolution == "reserved-owner")
        {
            return
                $"LibreHardwareMonitorLib 0.9.6 published more than one sensor at {libraryId}. " +
                "That id is the hardware path, the sensor type, and the index. " +
                "NvidiaGpu always constructs \"GPU Memory\" at load index 3, and a present NVAPI utilization domain uses the same index (domain 3 is \"GPU Bus\"). " +
                "\"GPU Memory\" keeps the bare id. That is the id already used when the extra row is absent, not a choice of whichever row was collected first. " +
                "Other distinct names are separate ids. Load stays percent. " +
                "Map the role to the published id you mean. " +
                "This is the library index collision. It is not a PawnIO detection failure, and this program does not ask you to change the driver. " +
                shown;
        }

        if (bareAssigned)
        {
            return $"Library id {DisplayId(libraryId)} was assigned to one distinguished row. {shown}";
        }

        if (resolution == "distinct-names")
        {
            return
                $"Library id {DisplayId(libraryId)} was shared by sensors with different names. " +
                "No row was given that bare id, because this host does not know which name owned the index. " +
                "Map a role to one of the published ids. Values were not merged and units were not changed. " +
                shown;
        }

        return
            $"Library id {DisplayId(libraryId)} was shared by sensors that cannot be told apart. " +
            "No value was published for the bare id. " +
            "Inventory rows use an unresolved suffix so the document can be written. That suffix is not the first or last reading. " +
            shown;
    }

    private static SensorIdentityNote Note(
        string resolution,
        string libraryId,
        bool bareAssigned,
        string message,
        IReadOnlyList<SensorIdentityMember> members)
    {
        return new SensorIdentityNote
        {
            LibraryId = libraryId,
            Resolution = resolution,
            BareIdAssigned = bareAssigned,
            Message = message,
            Members = members
        };
    }

    private static SensorIdentityMember Member(SensorSample sample, bool valuePublished)
    {
        return new SensorIdentityMember
        {
            Name = sample.Name,
            PublishedId = sample.Id,
            SensorType = sample.SensorType,
            NormalizedUnit = sample.NormalizedUnit,
            LibraryValue = sample.LibraryValue,
            ValuePublished = valuePublished && sample.State == SampleState.Fresh && sample.NormalizedValue is not null
        };
    }

    private static SensorSample Retarget(SensorSample sample, string id, string libraryId)
    {
        return sample with
        {
            Id = id,
            LibraryId = libraryId,
            Path = id.Split('/', StringSplitOptions.RemoveEmptyEntries)
        };
    }

    private static SensorSample Withhold(SensorSample sample, string id, string code, string message)
    {
        return sample with
        {
            Id = id,
            LibraryId = string.IsNullOrEmpty(sample.Id) ? null : sample.Id,
            Path = id.Split('/', StringSplitOptions.RemoveEmptyEntries),
            NormalizedValue = null,
            State = SampleState.Error,
            Error = new ReadingError(code, message)
        };
    }

    private static string NextFree(HashSet<string> used, string libraryId, string stem)
    {
        for (var number = 1; number < 10000; number++)
        {
            var id = Combine(libraryId, stem + "-" + number.ToString());
            if (used.Add(id))
            {
                return id;
            }
        }

        throw new InvalidOperationException("Could not allocate a unique diagnostic id.");
    }

    private static string Combine(string libraryId, string suffix)
    {
        if (string.IsNullOrEmpty(libraryId))
        {
            return "/unidentified/" + suffix;
        }

        return libraryId.TrimEnd('/') + "/" + suffix;
    }

    /// <summary>Stable name slug. "GPU Bus" becomes "gpu-bus". Empty when the name has no letters or digits.</summary>
    public static string Slug(string name)
    {
        var chars = new List<char>(name.Length);
        var dash = false;
        foreach (var character in name.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                chars.Add(character);
                dash = false;
            }
            else if (!dash && chars.Count > 0)
            {
                chars.Add('-');
                dash = true;
            }
        }

        while (chars.Count > 0 && chars[^1] == '-')
        {
            chars.RemoveAt(chars.Count - 1);
        }

        return new string(chars.ToArray());
    }

    private static string DisplayId(string libraryId)
    {
        return string.IsNullOrEmpty(libraryId) ? "(empty)" : libraryId;
    }

    private static string DisplayName(string name)
    {
        return name.Length == 0 ? "(empty name)" : "\"" + name + "\"";
    }

    private static IEnumerable<int> IndexRange(int count)
    {
        for (var index = 0; index < count; index++)
        {
            yield return index;
        }
    }
}

public sealed record IdentityResolution(
    IReadOnlyList<SensorSample> Sensors,
    IReadOnlyList<SensorIdentityNote> Notes);
