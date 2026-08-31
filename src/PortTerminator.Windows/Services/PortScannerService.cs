using System.Runtime.InteropServices;
using PortTerminator.Core.Helpers;
using PortTerminator.Core.Interfaces;
using PortTerminator.Core.Models;
using PortTerminator.Windows.Native;

namespace PortTerminator.Windows.Services;

public class PortScannerService : IPortScannerService
{
    private readonly IProcessInfoService _processInfoService;
    private readonly IRiskAssessmentService _riskAssessmentService;
    private readonly IWhitelistService _whitelistService;
    private readonly IRuleService _ruleService;

    public PortScannerService(
        IProcessInfoService processInfoService,
        IRiskAssessmentService riskAssessmentService,
        IWhitelistService whitelistService,
        IRuleService ruleService)
    {
        _processInfoService = processInfoService;
        _riskAssessmentService = riskAssessmentService;
        _whitelistService = whitelistService;
        _ruleService = ruleService;
    }

    public async Task<ServiceResult<PortSnapshot>> ScanAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var rawEntries = await Task.Run(ScanRawEntries, cancellationToken);
            var pids = rawEntries.Select(e => e.Pid).Where(p => p > 0).Distinct();
            var processCache = await _processInfoService.GetScanDetailsBatchAsync(pids, cancellationToken);

            var enriched = new List<PortEntry>(rawEntries.Count);
            foreach (var entry in rawEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var enrichedEntry = EnrichEntry(entry, processCache.GetValueOrDefault(entry.Pid));
                enrichedEntry.RiskLevel = _riskAssessmentService.Assess(
                    enrichedEntry, _whitelistService.Items, _ruleService.Rules);
                enrichedEntry.RiskDisplay = _riskAssessmentService.GetRiskDisplay(enrichedEntry.RiskLevel);
                enrichedEntry.IsWhitelisted = _whitelistService.IsWhitelisted(enrichedEntry);
                enriched.Add(enrichedEntry);
            }

            return ServiceResult<PortSnapshot>.Ok(new PortSnapshot
            {
                ScanTime = DateTime.Now,
                Entries = enriched.OrderBy(e => e.Port).ThenBy(e => e.Protocol).ToList()
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ServiceResult<PortSnapshot>.Fail(
                ServiceErrorCode.NativeApiError, $"端口扫描失败: {ex.Message}", ex);
        }
    }

    public Task<Dictionary<int, int>> GetPortCountsByPidAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ScanRawEntries()
                .Where(e => e.Pid > 0)
                .GroupBy(e => e.Pid)
                .ToDictionary(g => g.Key, g => g.Count());
        }, cancellationToken);

    private static List<PortEntry> ScanRawEntries()
    {
        var rawEntries = new List<PortEntry>();
        rawEntries.AddRange(ScanTcp(IpHlpApi.AfInet));
        rawEntries.AddRange(ScanTcp(IpHlpApi.AfInet6));
        rawEntries.AddRange(ScanUdp(IpHlpApi.AfInet));
        rawEntries.AddRange(ScanUdp(IpHlpApi.AfInet6));
        return rawEntries;
    }

    private static PortEntry EnrichEntry(PortEntry entry, ProcessDetails? details)
    {
        if (details is null)
        {
            if (entry.Pid > 0 && string.IsNullOrEmpty(entry.ProcessName))
                entry.ProcessName = $"PID {entry.Pid}";
            if (string.IsNullOrEmpty(entry.ExecutablePath))
                entry.ExecutablePath = entry.Pid > 0 ? "不可访问" : string.Empty;
            return entry;
        }

        entry.ProcessName = details.Identity.ProcessName;
        entry.ExecutablePath = string.IsNullOrEmpty(details.Identity.ExecutablePath)
            ? (details.IsAccessible ? "不可访问" : "权限不足")
            : details.Identity.ExecutablePath;
        entry.CommandLine = details.CommandLine;
        entry.ProcessStartTime = details.Identity.StartTime;
        entry.ProcessOwner = string.IsNullOrEmpty(details.Owner) ? "不可访问" : details.Owner;
        entry.DigitalSignature = details.DigitalSignature;
        entry.SignatureStatus = details.SignatureStatus;
        entry.IsSystemProcess = details.IsSystemProcess;
        entry.IsExternallyAccessible = entry.LocalAddress.StartsWith("0.0.0.0", StringComparison.Ordinal)
            || entry.LocalAddress.StartsWith("[::]", StringComparison.Ordinal)
            || entry.LocalAddress.StartsWith("::", StringComparison.OrdinalIgnoreCase);
        entry.Key = PortEntry.CreateKey(entry.Port, entry.Protocol, entry.LocalAddress, entry.Pid, entry.RemoteAddress, entry.RemotePort);
        return entry;
    }

    private static List<PortEntry> ScanTcp(int addressFamily)
    {
        var results = new List<PortEntry>();
        var size = 0;
        IpHlpApi.GetExtendedTcpTable(IntPtr.Zero, ref size, true, addressFamily, IpHlpApi.TcpTableClass.OwnerPidAll, 0);
        if (size <= 0) return results;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ret = IpHlpApi.GetExtendedTcpTable(buffer, ref size, true, addressFamily, IpHlpApi.TcpTableClass.OwnerPidAll, 0);
            if (ret != 0) return results;

            var count = Marshal.ReadInt32(buffer);
            if (addressFamily == IpHlpApi.AfInet)
            {
                var rowSize = Marshal.SizeOf<IpHlpApi.MIB_TCPROW_OWNER_PID>();
                for (var i = 0; i < count; i++)
                {
                    var rowPtr = IntPtr.Add(buffer, IpHlpApi.TableHeaderSize + i * rowSize);
                    var row = Marshal.PtrToStructure<IpHlpApi.MIB_TCPROW_OWNER_PID>(rowPtr);
                    results.Add(CreateTcpEntry(row.State, row.LocalAddr, row.LocalPort, row.RemoteAddr, row.RemotePort, row.OwningPid));
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<IpHlpApi.MIB_TCP6ROW_OWNER_PID>();
                for (var i = 0; i < count; i++)
                {
                    var rowPtr = IntPtr.Add(buffer, IpHlpApi.TableHeaderSize + i * rowSize);
                    var row = Marshal.PtrToStructure<IpHlpApi.MIB_TCP6ROW_OWNER_PID>(rowPtr);
                    var localAddr = IpHlpApi.FormatIpv6(row.LocalAddr, row.LocalScopeId);
                    var remoteAddr = IpHlpApi.FormatIpv6(row.RemoteAddr, row.RemoteScopeId);
                    var localPort = IpHlpApi.NetworkToHostPort(row.LocalPort);
                    var remotePort = IpHlpApi.NetworkToHostPort(row.RemotePort);
                    var state = MapTcpState(row.State);
                    var pid = IpHlpApi.SafePid(row.OwningPid);

                    var entry = new PortEntry
                    {
                        Port = localPort,
                        Protocol = PortProtocol.TCP,
                        LocalAddress = FormatAddressWithPort(localAddr, localPort),
                        RemoteAddress = FormatAddressWithPort(remoteAddr, remotePort),
                        RemotePort = remotePort,
                        State = state,
                        StateDisplay = PortStateHelper.GetStateDisplay(state),
                        Pid = pid,
                        ProcessName = pid > 0 ? string.Empty : "System"
                    };
                    entry.Key = PortEntry.CreateKey(entry.Port, entry.Protocol, entry.LocalAddress, entry.Pid, entry.RemoteAddress, entry.RemotePort);
                    results.Add(entry);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private static PortEntry CreateTcpEntry(uint state, uint localAddr, uint localPortRaw, uint remoteAddr, uint remotePortRaw, uint owningPid)
    {
        var localPort = IpHlpApi.NetworkToHostPort(localPortRaw);
        var remotePort = IpHlpApi.NetworkToHostPort(remotePortRaw);
        var local = IpHlpApi.FormatIpv4(localAddr);
        var remote = IpHlpApi.FormatIpv4(remoteAddr);
        var mappedState = MapTcpState(state);
        var pid = IpHlpApi.SafePid(owningPid);

        var entry = new PortEntry
        {
            Port = localPort,
            Protocol = PortProtocol.TCP,
            LocalAddress = $"{local}:{localPort}",
            RemoteAddress = $"{remote}:{remotePort}",
            RemotePort = remotePort,
            State = mappedState,
            StateDisplay = PortStateHelper.GetStateDisplay(mappedState),
            Pid = pid,
            ProcessName = pid > 0 ? string.Empty : "System"
        };
        entry.Key = PortEntry.CreateKey(entry.Port, entry.Protocol, entry.LocalAddress, entry.Pid, entry.RemoteAddress, entry.RemotePort);
        return entry;
    }

    private static List<PortEntry> ScanUdp(int addressFamily)
    {
        var results = new List<PortEntry>();
        var size = 0;
        IpHlpApi.GetExtendedUdpTable(IntPtr.Zero, ref size, true, addressFamily, IpHlpApi.UdpTableClass.OwnerPid, 0);
        if (size <= 0) return results;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ret = IpHlpApi.GetExtendedUdpTable(buffer, ref size, true, addressFamily, IpHlpApi.UdpTableClass.OwnerPid, 0);
            if (ret != 0) return results;

            var count = Marshal.ReadInt32(buffer);
            if (addressFamily == IpHlpApi.AfInet)
            {
                var rowSize = Marshal.SizeOf<IpHlpApi.MIB_UDPROW_OWNER_PID>();
                for (var i = 0; i < count; i++)
                {
                    var rowPtr = IntPtr.Add(buffer, IpHlpApi.TableHeaderSize + i * rowSize);
                    var row = Marshal.PtrToStructure<IpHlpApi.MIB_UDPROW_OWNER_PID>(rowPtr);
                    var localPort = IpHlpApi.NetworkToHostPort(row.LocalPort);
                    var localAddr = IpHlpApi.FormatIpv4(row.LocalAddr);
                    var pid = IpHlpApi.SafePid(row.OwningPid);

                    var entry = new PortEntry
                    {
                        Port = localPort,
                        Protocol = PortProtocol.UDP,
                        LocalAddress = $"{localAddr}:{localPort}",
                        State = PortState.Bound,
                        StateDisplay = "已绑定",
                        Pid = pid,
                        ProcessName = pid > 0 ? string.Empty : "System"
                    };
                    entry.Key = PortEntry.CreateKey(entry.Port, entry.Protocol, entry.LocalAddress, entry.Pid, entry.RemoteAddress, entry.RemotePort);
                    results.Add(entry);
                }
            }
            else
            {
                var rowSize = Marshal.SizeOf<IpHlpApi.MIB_UDP6ROW_OWNER_PID>();
                for (var i = 0; i < count; i++)
                {
                    var rowPtr = IntPtr.Add(buffer, IpHlpApi.TableHeaderSize + i * rowSize);
                    var row = Marshal.PtrToStructure<IpHlpApi.MIB_UDP6ROW_OWNER_PID>(rowPtr);
                    var localPort = IpHlpApi.NetworkToHostPort(row.LocalPort);
                    var localAddr = IpHlpApi.FormatIpv6(row.LocalAddr, row.LocalScopeId);
                    var pid = IpHlpApi.SafePid(row.OwningPid);

                    var entry = new PortEntry
                    {
                        Port = localPort,
                        Protocol = PortProtocol.UDP,
                        LocalAddress = FormatAddressWithPort(localAddr, localPort),
                        State = PortState.Bound,
                        StateDisplay = "已绑定",
                        Pid = pid,
                        ProcessName = pid > 0 ? string.Empty : "System"
                    };
                    entry.Key = PortEntry.CreateKey(entry.Port, entry.Protocol, entry.LocalAddress, entry.Pid, entry.RemoteAddress, entry.RemotePort);
                    results.Add(entry);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return results;
    }

    private static string FormatAddressWithPort(string address, int port)
    {
        if (address.Contains(':'))
            return $"[{address}]:{port}";
        return $"{address}:{port}";
    }

    private static PortState MapTcpState(uint state) => state switch
    {
        1 => PortState.Other,
        2 => PortState.TimeWait,
        3 => PortState.Other,
        4 => PortState.Other,
        5 => PortState.Established,
        6 => PortState.Other,
        7 => PortState.CloseWait,
        8 => PortState.Other,
        9 => PortState.Other,
        10 => PortState.Listening,
        11 => PortState.Other,
        12 => PortState.Other,
        _ => PortState.Unknown
    };
}
