namespace PortTerminator.Core.Models;

public enum RiskLevel
{
    Low,
    Medium,
    High
}

public enum PortProtocol
{
    TCP,
    UDP
}

public enum PortState
{
    Unknown,
    Listening,
    Established,
    TimeWait,
    CloseWait,
    Bound,
    Other
}

public class ProcessIdentity : IEquatable<ProcessIdentity>
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public DateTime? StartTime { get; init; }
    public string ExecutablePath { get; init; } = string.Empty;

    public bool Equals(ProcessIdentity? other)
    {
        if (other is null) return false;
        return Pid == other.Pid
               && string.Equals(ProcessName, other.ProcessName, StringComparison.OrdinalIgnoreCase)
               && StartTime == other.StartTime;
    }

    public override bool Equals(object? obj) => Equals(obj as ProcessIdentity);

    public override int GetHashCode() => HashCode.Combine(Pid, ProcessName.ToLowerInvariant(), StartTime);
}

public class ProcessDetails
{
    public ProcessIdentity Identity { get; init; } = new();
    public string CommandLine { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public int SessionId { get; set; }
    public string IntegrityLevel { get; set; } = string.Empty;
    public string DigitalSignature { get; set; } = string.Empty;
    public SignatureStatus SignatureStatus { get; set; } = SignatureStatus.Unknown;
    public bool IsSystemProcess { get; set; }
    public bool IsAccessible { get; set; } = true;
}

public enum SignatureStatus
{
    Unknown,
    Verified,
    Unsigned,
    CannotVerify
}
