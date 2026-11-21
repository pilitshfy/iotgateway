using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 设备变量服务
/// </summary>
[ApiDescriptionSettings(Order = 453)]
public class IotDeviceVariableService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;
    private readonly IServiceProvider _serviceProvider;

    public IotDeviceVariableService(SqlSugarRepository<IotDeviceVariable> variableRep, IServiceProvider serviceProvider)
    {
        _variableRep = variableRep;
        _serviceProvider = serviceProvider;
    }

    private IIotDeviceRuntime Runtime => _serviceProvider.GetService<IIotDeviceRuntime>();

    /// <summary>
    /// 变量分页列表
    /// </summary>
    [DisplayName("变量分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotDeviceVariable>> Page([FromBody] IotDeviceVariablePageInput input)
    {
        var paged = await _variableRep.AsQueryable()
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.Name.Contains(input.Keyword))
            .OrderBy(u => new { u.Index, u.Name })
            .ToPagedListAsync(input.Page, input.PageSize);
        FillRuntimeValues(paged.Items);
        return paged;
    }

    /// <summary>
    /// 某设备的变量列表
    /// </summary>
    [DisplayName("变量列表"), HttpGet]
    public async Task<List<IotDeviceVariable>> List([FromQuery] long deviceId)
    {
        var list = await _variableRep.AsQueryable()
            .Where(u => u.DeviceId == deviceId)
            .OrderBy(u => new { u.Index, u.Name })
            .ToListAsync();
        FillRuntimeValues(list);
        return list;
    }

    /// <summary>
    /// 批量写入变量值
    /// </summary>
    [DisplayName("批量写入变量"), ApiDescriptionSettings(Name = "Write"), HttpPost]
    public async Task<IotRpcResultDto> Write([FromBody] IotVariableWriteInput input)
    {
        if (input.Values == null || input.Values.Count == 0) throw Oops.Oh("请至少填写一个变量值");
        var device = await _variableRep.Context.Queryable<IotDevice>().InSingleAsync(input.DeviceId)
            ?? throw Oops.Oh("设备不存在");
        if (Runtime == null) throw Oops.Oh("设备运行时未启动");

        return await Runtime.ExecuteRpcAsync(new IotRpcRequestDto
        {
            DeviceName = device.DeviceName,
            Method = "write",
            RequestId = Guid.NewGuid().ToString(),
            Params = input.Values
        });
    }

    /// <summary>
    /// 新增变量
    /// </summary>
    [DisplayName("新增变量"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotDeviceVariable input)
    {
        input.Id = 0;
        await _variableRep.InsertAsync(input);
        if (Runtime != null) await Runtime.RestartDeviceAsync(input.DeviceId);
        return input.Id;
    }

    /// <summary>
    /// 修改变量
    /// </summary>
    [DisplayName("修改变量"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotDeviceVariable input)
    {
        var db = await _variableRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("变量不存在");
        var oldDeviceId = db.DeviceId;
        db.Name = input.Name;
        db.Description = input.Description;
        db.Method = input.Method;
        db.DeviceAddress = input.DeviceAddress;
        db.DataType = input.DataType;
        db.IsTrigger = input.IsTrigger;
        db.EndianType = input.EndianType;
        db.Expressions = input.Expressions;
        db.IsUpload = input.IsUpload;
        db.ProtectType = input.ProtectType;
        db.Index = input.Index;
        db.DeviceId = input.DeviceId;
        db.Alias = input.Alias;
        await _variableRep.AsUpdateable(db).ExecuteCommandAsync();
        if (Runtime != null)
        {
            await Runtime.RestartDeviceAsync(input.DeviceId);
            if (oldDeviceId != input.DeviceId) await Runtime.RestartDeviceAsync(oldDeviceId);
        }
    }

    /// <summary>
    /// 删除变量
    /// </summary>
    [DisplayName("删除变量"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        var db = await _variableRep.GetByIdAsync(input.Id);
        await _variableRep.DeleteByIdAsync(input.Id);
        if (db != null && Runtime != null) await Runtime.RestartDeviceAsync(db.DeviceId);
    }

    /// <summary>
    /// 批量删除变量
    /// </summary>
    [DisplayName("批量删除变量"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;
        var deviceIds = await _variableRep.AsQueryable().Where(u => input.Ids.Contains(u.Id)).Select(u => u.DeviceId).Distinct().ToListAsync();
        await _variableRep.Context.Deleteable<IotDeviceVariable>().In(input.Ids).ExecuteCommandAsync();
        if (Runtime != null)
            foreach (var deviceId in deviceIds)
                await Runtime.RestartDeviceAsync(deviceId);
    }

    private void FillRuntimeValues(IEnumerable<IotDeviceVariable> variables)
    {
        if (Runtime == null) return;
        foreach (var group in variables.GroupBy(u => u.DeviceId))
        {
            var runtimeVariables = Runtime.GetDeviceVariables(group.Key);
            foreach (var variable in group)
            {
                var current = runtimeVariables.FirstOrDefault(u => u.Id == variable.Id);
                if (current == null) continue;
                variable.Value = current.Value;
                variable.CookedValue = current.CookedValue;
                variable.Message = current.Message;
                variable.Timestamp = current.Timestamp;
                variable.StatusType = current.StatusType;
                variable.Values = current.Values;
                variable.CookedValues = current.CookedValues;
            }
        }
    }
}
