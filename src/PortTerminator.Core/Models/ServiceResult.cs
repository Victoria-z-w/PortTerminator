namespace PortTerminator.Core.Models;

public enum ServiceErrorCode
{
    None = 0,
    AccessDenied,
    ProcessNotFound,
    ProcessChanged,
    ProtectedProcess,
    AdministratorRequired,
    PortAlreadyReleased,
    InvalidRequest,
    NativeApiError,
    Unknown
}

public class ServiceResult
{
    public bool Success { get; init; }
    public ServiceErrorCode ErrorCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public Exception? Exception { get; init; }

    public static ServiceResult Ok(string message = "") =>
        new() { Success = true, ErrorCode = ServiceErrorCode.None, Message = message };

    public static ServiceResult Fail(ServiceErrorCode code, string message, Exception? ex = null) =>
        new() { Success = false, ErrorCode = code, Message = message, Exception = ex };
}

public class ServiceResult<T> : ServiceResult
{
    public T? Data { get; init; }

    public static ServiceResult<T> Ok(T data, string message = "") =>
        new() { Success = true, ErrorCode = ServiceErrorCode.None, Message = message, Data = data };

    public new static ServiceResult<T> Fail(ServiceErrorCode code, string message, Exception? ex = null) =>
        new() { Success = false, ErrorCode = code, Message = message, Exception = ex };
}
