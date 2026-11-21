using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 设备通讯配置服务
/// </summary>
[ApiDescriptionSettings(Order = 452)]
public class IotDeviceConfigService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDeviceConfig> _configRep;
    private readonly IServiceProvider _serviceProvider;

    public IotDeviceConfigService(SqlSugarRepository<IotDeviceConfig> configRep, IServiceProvider serviceProvider)
    {
        _configRep = configRep;
        _serviceProvider = serviceProvider;
    }

    private IIotDeviceRuntime Runtime => _serviceProvider.GetService<IIotDeviceRuntime>();

    /// <summary>
    /// 通讯配置分页列表
    /// </summary>
    [DisplayName("通讯配置分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotDeviceConfig>> Page([FromBody] IotDeviceConfigPageInput input)
    {
        return await _configRep.AsQueryable()
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.DeviceConfigName.Contains(input.Keyword))
            .OrderBy(u => u.Id)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    /// <summary>
    /// 某设备的通讯配置列表
    /// </summary>
    [DisplayName("通讯配置列表"), HttpGet]
    public async Task<List<IotDeviceConfig>> List([FromQuery] long deviceId)
    {
        return await _configRep.AsQueryable()
            .Where(u => u.DeviceId == deviceId)
            .OrderBy(u => u.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 新增通讯配置
    /// </summary>
    [DisplayName("新增通讯配置"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotDeviceConfig input)
    {
        input.Id = 0;
        await _configRep.InsertAsync(input);
        if (Runtime != null) await Runtime.RestartDeviceAsync(input.DeviceId);
        return input.Id;
    }

    /// <summary>
    /// 修改通讯配置
    /// </summary>
    [DisplayName("修改通讯配置"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotDeviceConfig input)
    {
        var db = await _configRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("通讯配置不存在");
        var oldDeviceId = db.DeviceId;
        db.DeviceConfigName = input.DeviceConfigName;
        db.DataSide = input.DataSide;
        db.Description = input.Description;
        db.Value = input.Value;
        db.EnumInfo = input.EnumInfo;
        db.DeviceId = input.DeviceId;
        await _configRep.AsUpdateable(db).ExecuteCommandAsync();
        if (Runtime != null)
        {
            await Runtime.RestartDeviceAsync(input.DeviceId);
            if (oldDeviceId != input.DeviceId) await Runtime.RestartDeviceAsync(oldDeviceId);
        }
    }

    /// <summary>
    /// 删除通讯配置
    /// </summary>
    [DisplayName("删除通讯配置"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        var db = await _configRep.GetByIdAsync(input.Id);
        await _configRep.DeleteByIdAsync(input.Id);
        if (db != null && Runtime != null) await Runtime.RestartDeviceAsync(db.DeviceId);
    }

    /// <summary>
    /// 批量删除通讯配置
    /// </summary>
    [DisplayName("批量删除通讯配置"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;
        var deviceIds = await _configRep.AsQueryable().Where(u => input.Ids.Contains(u.Id)).Select(u => u.DeviceId).Distinct().ToListAsync();
        await _configRep.Context.Deleteable<IotDeviceConfig>().In(input.Ids).ExecuteCommandAsync();
        if (Runtime != null)
            foreach (var deviceId in deviceIds)
                await Runtime.RestartDeviceAsync(deviceId);
    }
}
