using PortTerminator.Core.Models;

namespace PortTerminator.Core.Helpers;

public static class PortStateHelper
{
    public static string GetStateDisplay(PortState state) => state switch
    {
        PortState.Listening => "监听中",
        PortState.Established => "已连接",
        PortState.TimeWait => "TIME_WAIT",
        PortState.CloseWait => "CLOSE_WAIT",
        PortState.Bound => "已绑定",
        PortState.Other => "其他",
        _ => "未知"
    };

    public static string FormatUptime(DateTime? startTime)
    {
        if (startTime is null) return "--";
        var span = DateTime.Now - startTime.Value;
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}天 {span.Hours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
        return $"{span.Hours:D2}:{span.Minutes:D2}:{span.Seconds:D2}";
    }
}
