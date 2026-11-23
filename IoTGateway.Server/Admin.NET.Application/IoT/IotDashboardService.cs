using System.ComponentModel;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 首页图表服务（对齐原 HomeController 设备/变量状态图）
/// </summary>
[ApiDescriptionSettings(Order = 456)]
public class IotDashboardService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDevice> _deviceRep;
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly IServiceProvider _serviceProvider;

    public IotDashboardService(
        SqlSugarRepository<IotDevice> deviceRep,
        SqlSugarRepository<IotDeviceVariable> variableRep,
        IotOrgScopeService orgScope,
        IServiceProvider serviceProvider)
    {
        _deviceRep = deviceRep;
        _variableRep = variableRep;
        _orgScope = orgScope;
        _serviceProvider = serviceProvider;
    }

    private IIotDeviceRuntime Runtime => _serviceProvider.GetService<IIotDeviceRuntime>();

    /// <summary>
    /// 设备状态图表（停止/运行/异常）
    /// </summary>
    [DisplayName("设备状态图表"), HttpGet]
    public async Task<List<IotChartDataDto>> DeviceStatusChart()
    {
        var devices = await (await _orgScope.DeviceQueryAsync())
            .Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device)
            .ToListAsync();
        var runningIds = Runtime?.GetRunningDeviceIds() ?? new List<long>();

        var stopped = devices.Count(u => !u.AutoStart);
        var running = devices.Count(u => u.AutoStart && runningIds.Contains(u.Id));
        var abnormal = devices.Count(u => u.AutoStart && !runningIds.Contains(u.Id));

        return new List<IotChartDataDto>
        {
            new() { Category = "停止", Series = "Device", Value = stopped },
            new() { Category = "运行", Series = "Device", Value = running },
            new() { Category = "异常", Series = "Device", Value = abnormal }
        };
    }

    /// <summary>
    /// 设备变量状态图表（按设备统计正常/异常变量数）
    /// </summary>
    [DisplayName("设备变量图表"), HttpGet]
    public async Task<List<IotVariableChartDto>> VariableStatusChart()
    {
        var devices = await (await _orgScope.DeviceQueryAsync())
            .Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device)
            .OrderBy(u => new { u.Index, u.DeviceName })
            .ToListAsync();
        if (devices.Count == 0) return new List<IotVariableChartDto>();

        var deviceIds = devices.Select(u => u.Id).ToList();
        var totals = await _variableRep.AsQueryable()
            .Where(u => deviceIds.Contains(u.DeviceId))
            .GroupBy(u => u.DeviceId)
            .Select(u => new { u.DeviceId, Count = SqlFunc.AggregateCount(u.Id) })
            .ToListAsync();

        var list = new List<IotVariableChartDto>();
        foreach (var device in devices)
        {
            var total = totals.FirstOrDefault(u => u.DeviceId == device.Id)?.Count ?? 0;
            var runtimeVariables = Runtime?.GetDeviceVariables(device.Id) ?? new List<IotDeviceVariable>();
            var good = runtimeVariables.Count(u => u.StatusType == IotVariableStatusEnum.Good);
            list.Add(new IotVariableChartDto
            {
                DeviceName = device.DeviceName,
                GoodCount = good,
                BadCount = Math.Max(total - good, 0)
            });
        }

        return list;
    }
}
