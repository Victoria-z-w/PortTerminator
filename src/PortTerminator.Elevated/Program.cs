using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PortTerminator.Core.Models;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("PortTerminator.Elevated only runs on Windows.");
    return 1;
}

var pipeName = GetArg("--pipe");
var sessionToken = GetArg("--token");

if (string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(sessionToken))
{
    Console.Error.WriteLine("Missing --pipe or --token argument.");
    return 1;
}

var pipeSecurity = new PipeSecurity();
var currentUser = WindowsIdentity.GetCurrent().User;
if (currentUser is not null)
{
    pipeSecurity.AddAccessRule(new PipeAccessRule(currentUser, PipeAccessRights.ReadWrite, AccessControlType.Allow));
}

await using var server = NamedPipeServerStreamAcl.Create(
    pipeName,
    PipeDirection.InOut,
    1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous,
    4096,
    4096,
    pipeSecurity);

await server.WaitForConnectionAsync();

var buffer = new byte[8192];
var read = await server.ReadAsync(buffer);
var requestJson = Encoding.UTF8.GetString(buffer, 0, read);
var request = JsonSerializer.Deserialize<ElevatedRequest>(requestJson);

var response = HandleRequest(request, sessionToken);
var responseJson = JsonSerializer.Serialize(response);
var responseBytes = Encoding.UTF8.GetBytes(responseJson);
await server.WriteAsync(responseBytes);
await server.FlushAsync();

return response.Success ? 0 : 1;

static ElevatedResponse HandleRequest(ElevatedRequest? request, string expectedToken)
{
    if (request is null)
        return Fail(request, ServiceErrorCode.InvalidRequest, "无效请求");

    if (!string.Equals(request.SessionToken, expectedToken, StringComparison.Ordinal))
        return Fail(request, ServiceErrorCode.InvalidRequest, "会话令牌无效");

    if (!VerifyProcessIdentity(request))
        return Fail(request, ServiceErrorCode.ProcessChanged, "进程身份验证失败，目标可能已变化");

    try
    {
        using var process = Process.GetProcessById(request.Pid);
        switch (request.Command)
        {
            case ElevatedCommand.KillProcess:
                process.Kill(entireProcessTree: false);
                break;
            case ElevatedCommand.KillProcessTree:
                process.Kill(entireProcessTree: true);
                break;
            case ElevatedCommand.QueryPrivilegedProcess:
                return new ElevatedResponse
                {
                    RequestId = request.RequestId,
                    Success = true,
                    ErrorCode = ServiceErrorCode.None,
                    Message = process.ProcessName
                };
        }

        return new ElevatedResponse
        {
            RequestId = request.RequestId,
            Success = true,
            ErrorCode = ServiceErrorCode.None,
            Message = "操作成功"
        };
    }
    catch (ArgumentException)
    {
        return Fail(request, ServiceErrorCode.ProcessNotFound, "进程不存在");
    }
    catch (Exception ex)
    {
        return Fail(request, ServiceErrorCode.Unknown, ex.Message);
    }
}

static bool VerifyProcessIdentity(ElevatedRequest request)
{
    try
    {
        using var process = Process.GetProcessById(request.Pid);
        var name = process.ProcessName;
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        if (!string.Equals(name, request.ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (request.ProcessStartTime.HasValue)
        {
            try
            {
                if (process.StartTime != request.ProcessStartTime.Value)
                    return false;
            }
            catch
            {
                return false;
            }
        }

        return true;
    }
    catch
    {
        return false;
    }
}

static ElevatedResponse Fail(ElevatedRequest? request, ServiceErrorCode code, string message) =>
    new()
    {
        RequestId = request?.RequestId ?? Guid.Empty,
        Success = false,
        ErrorCode = code,
        Message = message
    };

static string? GetArg(string name)
{
    var args = Environment.GetCommandLineArgs();
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1].Trim('"');
    }
    return null;
}
