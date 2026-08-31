using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Core.Services;

public class PortSnapshotComparer : IPortSnapshotComparer
{
    public PortSnapshotDiff Compare(PortSnapshot? previous, PortSnapshot current)
    {
        var prevDict = previous?.ToDictionary() ?? new Dictionary<string, PortEntry>(StringComparer.OrdinalIgnoreCase);
        var currDict = current.ToDictionary();

        var added = new List<PortEntry>();
        var removed = new List<PortEntry>();
        var updated = new List<(PortEntry Old, PortEntry New)>();

        foreach (var (key, entry) in currDict)
        {
            if (!prevDict.TryGetValue(key, out var old))
            {
                added.Add(entry);
                continue;
            }

            if (HasChanged(old, entry))
                updated.Add((old, entry));
        }

        foreach (var (key, entry) in prevDict)
        {
            if (!currDict.ContainsKey(key))
                removed.Add(entry);
        }

        return new PortSnapshotDiff
        {
            Added = added,
            Removed = removed,
            Updated = updated
        };
    }

    public IReadOnlyList<PortChangeEvent> DetectChanges(PortSnapshotDiff diff)
    {
        var events = new List<PortChangeEvent>();

        foreach (var entry in diff.Added)
        {
            events.Add(new PortChangeEvent
            {
                ChangeType = PortChangeType.NewPort,
                Port = entry
            });
        }

        foreach (var entry in diff.Removed)
        {
            events.Add(new PortChangeEvent
            {
                ChangeType = PortChangeType.PortClosed,
                Port = entry
            });
        }

        foreach (var (old, @new) in diff.Updated)
        {
            if (old.Pid != @new.Pid || !string.Equals(old.ProcessName, @new.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new PortChangeEvent
                {
                    ChangeType = PortChangeType.ProcessChanged,
                    Port = @new,
                    PreviousPort = old
                });
            }

            if (old.RiskLevel != @new.RiskLevel)
            {
                events.Add(new PortChangeEvent
                {
                    ChangeType = PortChangeType.RiskChanged,
                    Port = @new,
                    PreviousPort = old
                });
            }
        }

        return events;
    }

    private static bool HasChanged(PortEntry old, PortEntry @new) =>
        old.Pid != @new.Pid
        || !string.Equals(old.ProcessName, @new.ProcessName, StringComparison.OrdinalIgnoreCase)
        || old.State != @new.State
        || old.RiskLevel != @new.RiskLevel
        || !string.Equals(old.LocalAddress, @new.LocalAddress, StringComparison.OrdinalIgnoreCase);
}
