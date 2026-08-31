namespace PortTerminator.Core.Models;

public class PortEntry
{
    public string Key { get; set; } = string.Empty;
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; }
    public string LocalAddress { get; set; } = string.Empty;
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public PortState State { get; set; }
    public string StateDisplay { get; set; } = string.Empty;
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string CommandLine { get; set; } = string.Empty;
    public DateTime? ProcessStartTime { get; set; }
    public string ProcessOwner { get; set; } = string.Empty;
    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;
    public string RiskDisplay { get; set; } = "低";
    public string DigitalSignature { get; set; } = string.Empty;
    public SignatureStatus SignatureStatus { get; set; }
    public bool IsSystemProcess { get; set; }
    public bool IsWhitelisted { get; set; }
    public bool IsExternallyAccessible { get; set; }
    public ProcessIdentity Identity => new()
    {
        Pid = Pid,
        ProcessName = ProcessName,
        StartTime = ProcessStartTime,
        ExecutablePath = ExecutablePath
    };

    public static string CreateKey(int port, PortProtocol protocol, string localAddress, int pid, string remoteAddress = "", int remotePort = 0) =>
        $"{protocol}:{localAddress}:{port}:{remoteAddress}:{remotePort}:{pid}";
}

public class PortSnapshot
{
    public DateTime ScanTime { get; init; } = DateTime.Now;
    public IReadOnlyList<PortEntry> Entries { get; init; } = Array.Empty<PortEntry>();

    public Dictionary<string, PortEntry> ToDictionary() =>
        Entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
}

public class PortSnapshotDiff
{
    public IReadOnlyList<PortEntry> Added { get; init; } = Array.Empty<PortEntry>();
    public IReadOnlyList<PortEntry> Removed { get; init; } = Array.Empty<PortEntry>();
    public IReadOnlyList<(PortEntry Old, PortEntry New)> Updated { get; init; } =
        Array.Empty<(PortEntry, PortEntry)>();
}

public enum PortChangeType
{
    NewPort,
    PortClosed,
    ProcessChanged,
    RiskChanged
}

public class PortChangeEvent
{
    public PortChangeType ChangeType { get; init; }
    public PortEntry? Port { get; init; }
    public PortEntry? PreviousPort { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}
