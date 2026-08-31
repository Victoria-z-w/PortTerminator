namespace PortTerminator.Core.Models;

public enum ElevatedCommand
{
    KillProcess,
    KillProcessTree,
    QueryPrivilegedProcess
}

public class ElevatedRequest
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public ElevatedCommand Command { get; set; }
    public int Pid { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime? ProcessStartTime { get; set; }
    public string SessionToken { get; set; } = string.Empty;
}

public class ElevatedResponse
{
    public Guid RequestId { get; set; }
    public bool Success { get; set; }
    public ServiceErrorCode ErrorCode { get; set; }
    public string Message { get; set; } = string.Empty;
}
