using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Reflection;
using Furion.FriendlyException;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 设备通讯配置服务
/// </summary>
[ApiDescriptionSettings(Order = 452)]
public class IotDeviceConfigService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDeviceConfig> _configRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly IServiceProvider _serviceProvider;

    public IotDeviceConfigService(SqlSugarRepository<IotDeviceConfig> configRep, IotOrgScopeService orgScope, IServiceProvider serviceProvider)
    {
        _configRep = configRep;
        _orgScope = orgScope;
        _serviceProvider = serviceProvider;
    }

    private IIotDeviceRuntime Runtime => _serviceProvider.GetService<IIotDeviceRuntime>();

    /// <summary>
    /// 通讯配置分页列表
    /// </summary>
    [DisplayName("通讯配置分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotDeviceConfig>> Page([FromBody] IotDeviceConfigPageInput input)
    {
        if (input.DeviceId.HasValue) await _orgScope.EnsureDeviceAccessAsync(input.DeviceId.Value);
        var deviceIds = await (await _orgScope.DeviceQueryAsync()).Select(u => u.Id).ToListAsync();
        return await _configRep.AsQueryable()
            .Where(u => deviceIds.Contains(u.DeviceId))
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
        await _orgScope.EnsureDeviceAccessAsync(deviceId);
        return await _configRep.AsQueryable()
            .Where(u => u.DeviceId == deviceId)
            .OrderBy(u => u.Id)
            .ToListAsync();
    }

    /// <summary>
    /// 批量修改通讯配置
    /// </summary>
    [DisplayName("批量修改通讯配置"), ApiDescriptionSettings(Name = "BatchUpdate"), HttpPost]
    public async Task BatchUpdate([FromBody] IotDeviceConfigBatchUpdateInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;

        var ids = input.Ids.Distinct().ToList();
        var configs = await _configRep.AsQueryable().Where(u => ids.Contains(u.Id)).ToListAsync();
        if (configs.Count != ids.Count) throw Oops.Oh("部分通讯配置不存在或无权访问");
        await _orgScope.GetAccessibleDevicesAsync(configs.Select(u => u.DeviceId));
        foreach (var db in configs)
        {
            if (input.DataSide.HasValue) db.DataSide = input.DataSide.Value;
            if (input.Value != null) db.Value = input.Value;
            if (input.Description != null) db.Description = input.Description;
            db.UpdateTime = DateTime.Now;
            await _configRep.AsUpdateable(db).ExecuteCommandAsync();
        }

        if (Runtime != null)
            foreach (var deviceId in configs.Select(u => u.DeviceId).Distinct())
                await Runtime.RestartDeviceAsync(deviceId);
    }

    /// <summary>
    /// 导出通讯配置 Excel
    /// </summary>
    [DisplayName("导出通讯配置"), ApiDescriptionSettings(Name = "Export"), HttpGet]
    public async Task<IActionResult> Export([FromQuery] long? deviceId)
    {
        if (deviceId.HasValue) await _orgScope.EnsureDeviceAccessAsync(deviceId.Value);
        var devices = await (await _orgScope.DeviceQueryAsync()).ToListAsync();
        var deviceIds = devices.Select(u => u.Id).ToList();
        var configs = await _configRep.AsQueryable()
            .Where(u => deviceIds.Contains(u.DeviceId))
            .WhereIF(deviceId.HasValue, u => u.DeviceId == deviceId)
            .OrderBy(u => new { u.DeviceId, u.DeviceConfigName })
            .ToListAsync();

        var table = new DataTable("通讯配置");
        foreach (var column in new[] { "设备名", "名称", "属性侧", "描述", "值", "备注" })
            table.Columns.Add(column);

        var deviceNames = devices.ToDictionary(u => u.Id, u => u.DeviceName);
        foreach (var config in configs)
        {
            table.Rows.Add(deviceNames.GetValueOrDefault(config.DeviceId), config.DeviceConfigName,
                config.DataSide == IotDataSideEnum.AnySide ? "共享属性" : "客户端", config.Description, config.Value, config.EnumInfo);
        }

        await using var stream = new MemoryStream();
        await stream.SaveAsAsync(table, printHeader: true);
        return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = $"iotgateway_configs_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
        };
    }

    /// <summary>
    /// 新增通讯配置
    /// </summary>
    [DisplayName("新增通讯配置"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotDeviceConfig input)
    {
        await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
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
        await _orgScope.EnsureDeviceAccessAsync(db.DeviceId);
        await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
        var oldDeviceId = db.DeviceId;
        db.DeviceConfigName = input.DeviceConfigName;
        db.DataSide = input.DataSide;
        db.Description = input.Description;
        db.Value = input.Value;
        db.EnumInfo = input.EnumInfo;
        db.DeviceId = input.DeviceId;
        db.UpdateTime = DateTime.Now;
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
        var db = await _configRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("通讯配置不存在");
        await _orgScope.EnsureDeviceAccessAsync(db.DeviceId);
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
        var ids = input.Ids.Distinct().ToList();
        var configs = await _configRep.AsQueryable().Where(u => ids.Contains(u.Id)).ToListAsync();
        if (configs.Count != ids.Count) throw Oops.Oh("部分通讯配置不存在或无权访问");
        var deviceIds = configs.Select(u => u.DeviceId).Distinct().ToList();
        await _orgScope.GetAccessibleDevicesAsync(deviceIds);
        await _configRep.Context.Deleteable<IotDeviceConfig>().In(ids).ExecuteCommandAsync();
        if (Runtime != null)
            foreach (var deviceId in deviceIds)
                await Runtime.RestartDeviceAsync(deviceId);
    }
}
