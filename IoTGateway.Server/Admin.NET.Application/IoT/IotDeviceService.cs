using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 设备服务
/// </summary>
[ApiDescriptionSettings(Order = 450)]
public class IotDeviceService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDevice> _deviceRep;
    private readonly SqlSugarRepository<IotDriver> _driverRep;
    private readonly SqlSugarRepository<IotDeviceConfig> _configRep;
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;
    private readonly IServiceProvider _serviceProvider;

    public IotDeviceService(
        SqlSugarRepository<IotDevice> deviceRep,
        SqlSugarRepository<IotDriver> driverRep,
        SqlSugarRepository<IotDeviceConfig> configRep,
        SqlSugarRepository<IotDeviceVariable> variableRep,
        IServiceProvider serviceProvider)
    {
        _deviceRep = deviceRep;
        _driverRep = driverRep;
        _configRep = configRep;
        _variableRep = variableRep;
        _serviceProvider = serviceProvider;
    }

    private IIotDeviceRuntime Runtime => _serviceProvider.GetService<IIotDeviceRuntime>();

    /// <summary>
    /// 设备分页列表
    /// </summary>
    [DisplayName("设备分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotDevice>> Page([FromBody] IotDevicePageInput input)
    {
        var paged = await _deviceRep.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.DeviceName.Contains(input.Keyword))
            .WhereIF(input.DriverId.HasValue, u => u.DriverId == input.DriverId)
            .WhereIF(input.DeviceTypeEnum.HasValue, u => u.DeviceTypeEnum == input.DeviceTypeEnum)
            .WhereIF(input.ParentId.HasValue, u => u.ParentId == input.ParentId)
            .OrderBy(u => new { u.Index, u.DeviceName })
            .ToPagedListAsync(input.Page, input.PageSize);

        await FillDriverNameAsync(paged.Items);
        return paged;
    }

    /// <summary>
    /// 设备树列表（全部）
    /// </summary>
    [DisplayName("设备树列表"), HttpGet]
    public async Task<List<IotDevice>> List([FromQuery] string keyword = null)
    {
        var list = await _deviceRep.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), u => u.DeviceName.Contains(keyword))
            .OrderBy(u => new { u.Index, u.DeviceName })
            .ToListAsync();

        await FillDriverNameAsync(list);
        return list;
    }

    /// <summary>
    /// 设备详情（含通讯配置与变量）
    /// </summary>
    [DisplayName("设备详情"), HttpGet]
    public async Task<IotDevice> Detail([FromQuery] long id)
    {
        var entity = await _deviceRep.GetByIdAsync(id);
        if (entity == null) throw Oops.Oh("设备不存在");

        entity.DeviceConfigs = await _configRep.AsQueryable().Where(u => u.DeviceId == id).OrderBy(u => u.Id).ToListAsync();
        entity.DeviceVariables = await _variableRep.AsQueryable().Where(u => u.DeviceId == id).OrderBy(u => u.Index).ToListAsync();
        await FillDriverNameAsync(new[] { entity });
        return entity;
    }

    /// <summary>
    /// 新增设备
    /// </summary>
    [DisplayName("新增设备"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotDevice input)
    {
        input.Id = 0;
        await _deviceRep.InsertAsync(input);
        if (Runtime != null) await Runtime.RestartDeviceAsync(input.Id);
        return input.Id;
    }

    /// <summary>
    /// 修改设备
    /// </summary>
    [DisplayName("修改设备"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotDevice input)
    {
        var db = await _deviceRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("设备不存在");
        db.DeviceName = input.DeviceName;
        db.Index = input.Index;
        db.Description = input.Description;
        db.ParentId = input.ParentId;
        db.DriverId = input.DriverId;
        db.AutoStart = input.AutoStart;
        db.CgUpload = input.CgUpload;
        db.EnforcePeriod = input.EnforcePeriod;
        db.CmdPeriod = input.CmdPeriod;
        db.DeviceTypeEnum = input.DeviceTypeEnum;
        await _deviceRep.AsUpdateable(db).ExecuteCommandAsync();
        if (Runtime != null) await Runtime.RestartDeviceAsync(input.Id);
    }

    /// <summary>
    /// 删除设备（同时删除通讯配置与变量）
    /// </summary>
    [DisplayName("删除设备"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        if (Runtime != null) await Runtime.StopDeviceAsync(input.Id);
        await DeleteChildrenAsync(new List<long> { input.Id });
        await _deviceRep.DeleteByIdAsync(input.Id);
    }

    /// <summary>
    /// 批量删除设备
    /// </summary>
    [DisplayName("批量删除设备"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;

        if (Runtime != null)
            foreach (var deviceId in input.Ids)
                await Runtime.StopDeviceAsync(deviceId);

        await DeleteChildrenAsync(input.Ids);
        await _deviceRep.Context.Deleteable<IotDevice>().In(input.Ids).ExecuteCommandAsync();
    }

    /// <summary>
    /// 仪表盘统计
    /// </summary>
    [DisplayName("仪表盘统计"), HttpGet]
    public async Task<object> Stats()
    {
        var deviceCount = await _deviceRep.AsQueryable().Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device).CountAsync();
        var groupCount = await _deviceRep.AsQueryable().Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Group).CountAsync();
        var variableCount = await _variableRep.AsQueryable().CountAsync();
        var configCount = await _configRep.AsQueryable().CountAsync();
        var driverCount = await _driverRep.AsQueryable().CountAsync();
        var online = Runtime?.GetRunningDeviceIds()?.Count ?? 0;

        return new
        {
            DeviceCount = deviceCount,
            GroupCount = groupCount,
            OnlineCount = online,
            OfflineCount = Math.Max(deviceCount - online, 0),
            VariableCount = variableCount,
            ConfigCount = configCount,
            DriverCount = driverCount
        };
    }

    /// <summary>
    /// 驱动下拉列表
    /// </summary>
    [DisplayName("驱动下拉列表"), HttpGet]
    public async Task<List<IotDriver>> DriverList()
    {
        return await _driverRep.AsQueryable().OrderBy(u => u.DriverName).ToListAsync();
    }

    private async Task DeleteChildrenAsync(List<long> deviceIds)
    {
        await _configRep.Context.Deleteable<IotDeviceConfig>().In(u => u.DeviceId, deviceIds).ExecuteCommandAsync();
        await _variableRep.Context.Deleteable<IotDeviceVariable>().In(u => u.DeviceId, deviceIds).ExecuteCommandAsync();
    }

    private async Task FillDriverNameAsync(IEnumerable<IotDevice> devices)
    {
        var list = devices?.ToList();
        if (list == null || list.Count == 0) return;

        var driverIds = list.Where(u => u.DriverId.HasValue).Select(u => u.DriverId.Value).Distinct().ToList();
        if (driverIds.Count == 0) return;

        var drivers = await _driverRep.AsQueryable().Where(u => driverIds.Contains(u.Id)).ToListAsync();
        foreach (var device in list)
            device.DriverName = drivers.FirstOrDefault(u => u.Id == device.DriverId)?.DriverName;
    }
}
