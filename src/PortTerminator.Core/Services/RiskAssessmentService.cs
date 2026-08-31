using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;

namespace PortTerminator.Core.Services;

public class RiskAssessmentService : IRiskAssessmentService
{
    private static readonly HashSet<int> DevPorts = new() { 3000, 5173, 8080, 8000, 8888 };
    private static readonly HashSet<int> SensitivePorts = new()
    {
        21, 22, 23, 135, 139, 445, 1433, 3306, 3389, 5432, 6379, 9200, 27017
    };

    public RiskLevel Assess(PortEntry entry, IEnumerable<WhitelistItem> whitelist, IEnumerable<PortRule> rules)
    {
        if (IsWhitelisted(entry, whitelist))
            return RiskLevel.Low;

        foreach (var rule in rules.Where(r => r.IsEnabled))
        {
            if (MatchesRule(entry, rule))
                return rule.RiskLevel;
        }

        var isAllInterfaces = IsAllInterfaces(entry.LocalAddress);
        var isLocalOnly = IsLocalOnly(entry.LocalAddress);

        if (SensitivePorts.Contains(entry.Port) && isAllInterfaces)
            return RiskLevel.High;

        if (DevPorts.Contains(entry.Port) && isAllInterfaces)
            return RiskLevel.Medium;

        if (isAllInterfaces && entry.State is PortState.Listening or PortState.Bound)
            return RiskLevel.Medium;

        if (isLocalOnly)
            return RiskLevel.Low;

        return RiskLevel.Low;
    }

    public string GetRiskDisplay(RiskLevel level) => level switch
    {
        RiskLevel.Low => "低",
        RiskLevel.Medium => "中",
        RiskLevel.High => "高",
        _ => "低"
    };

    private static bool IsWhitelisted(PortEntry entry, IEnumerable<WhitelistItem> whitelist)
    {
        foreach (var item in whitelist.Where(w => w.IsEnabled))
        {
            switch (item.Type)
            {
                case WhitelistType.Port when item.Value == entry.Port.ToString():
                    return true;
                case WhitelistType.ProcessName when string.Equals(item.Value, entry.ProcessName, StringComparison.OrdinalIgnoreCase):
                    return true;
                case WhitelistType.ExecutablePath when string.Equals(item.Value, entry.ExecutablePath, StringComparison.OrdinalIgnoreCase):
                    return true;
            }
        }
        return false;
    }

    private static bool MatchesRule(PortEntry entry, PortRule rule)
    {
        if (rule.Port.HasValue && rule.Port.Value != entry.Port)
            return false;

        if (!string.IsNullOrEmpty(rule.ProcessNameContains)
            && !entry.ProcessName.Contains(rule.ProcessNameContains, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(rule.ListenAddress)
            && !entry.LocalAddress.StartsWith(rule.ListenAddress, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsAllInterfaces(string localAddress)
    {
        if (string.IsNullOrEmpty(localAddress)) return false;
        return localAddress.StartsWith("0.0.0.0", StringComparison.Ordinal)
               || localAddress.StartsWith("[::]", StringComparison.Ordinal)
               || localAddress.StartsWith("::", StringComparison.Ordinal);
    }

    private static bool IsLocalOnly(string localAddress)
    {
        if (string.IsNullOrEmpty(localAddress)) return false;
        return localAddress.StartsWith("127.0.0.1", StringComparison.Ordinal)
               || localAddress.StartsWith("[::1]", StringComparison.Ordinal)
               || localAddress.StartsWith("localhost", StringComparison.OrdinalIgnoreCase);
    }
}
