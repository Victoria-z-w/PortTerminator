using System.Text;
using System.Text.Json;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Windows.Services;

public class ExportService : IExportService
{
    public async Task<ServiceResult<string>> ExportPortsAsync(
        IEnumerable<PortEntry> ports, string format, string filePath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var list = ports.ToList();
                if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
                {
                    var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json, Encoding.UTF8);
                }
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("端口,协议,本地地址,PID,进程,路径,状态,风险,扫描时间");
                    var scanTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    foreach (var p in list)
                    {
                        sb.AppendLine($"{p.Port},{p.Protocol},{Escape(p.LocalAddress)},{p.Pid},{Escape(p.ProcessName)},{Escape(p.ExecutablePath)},{Escape(p.StateDisplay)},{p.RiskDisplay},{scanTime}");
                    }
                    File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
                }

                return ServiceResult<string>.Ok(filePath, "导出成功");
            }
            catch (Exception ex)
            {
                return ServiceResult<string>.Fail(ServiceErrorCode.Unknown, ex.Message, ex);
            }
        }, cancellationToken);
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
