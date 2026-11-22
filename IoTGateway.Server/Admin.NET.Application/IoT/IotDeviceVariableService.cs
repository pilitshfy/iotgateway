using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Reflection;
using Furion.FriendlyException;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;
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
    /// 批量修改变量
    /// </summary>
    [DisplayName("批量修改变量"), ApiDescriptionSettings(Name = "BatchUpdate"), HttpPost]
    public async Task BatchUpdate([FromBody] IotDeviceVariableBatchUpdateInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;

        var variables = await _variableRep.AsQueryable().Where(u => input.Ids.Contains(u.Id)).ToListAsync();
        foreach (var db in variables)
        {
            if (!string.IsNullOrWhiteSpace(input.Method)) db.Method = input.Method;
            if (input.DataType.HasValue) db.DataType = input.DataType.Value;
            if (input.EndianType.HasValue) db.EndianType = input.EndianType.Value;
            if (input.IsTrigger.HasValue) db.IsTrigger = input.IsTrigger.Value;
            if (input.IsUpload.HasValue) db.IsUpload = input.IsUpload.Value;
            if (input.ProtectType.HasValue) db.ProtectType = input.ProtectType.Value;
            if (!string.IsNullOrWhiteSpace(input.Alias)) db.Alias = input.Alias;
            if (!string.IsNullOrWhiteSpace(input.Expressions)) db.Expressions = input.Expressions;
            if (input.Index.HasValue) db.Index = input.Index.Value;
            await _variableRep.AsUpdateable(db).ExecuteCommandAsync();
        }

        if (Runtime != null)
            foreach (var deviceId in variables.Select(u => u.DeviceId).Distinct())
                await Runtime.RestartDeviceAsync(deviceId);
    }

    /// <summary>
    /// 导出变量 Excel
    /// </summary>
    [DisplayName("导出变量"), ApiDescriptionSettings(Name = "Export"), HttpGet]
    public async Task<IActionResult> Export([FromQuery] long? deviceId)
    {
        var devices = await _variableRep.Context.Queryable<IotDevice>().ToListAsync();
        var variables = await _variableRep.AsQueryable()
            .WhereIF(deviceId.HasValue, u => u.DeviceId == deviceId)
            .OrderBy(u => new { u.DeviceId, u.Index, u.Name })
            .ToListAsync();

        var table = new DataTable("变量配置");
        foreach (var column in new[] { "设备名", "变量名", "方法", "地址", "类型", "大小端", "表达式", "别名", "上传", "排序", "触发", "权限", "描述" })
            table.Columns.Add(column);

        var deviceNames = devices.ToDictionary(u => u.Id, u => u.DeviceName);
        foreach (var variable in variables)
        {
            table.Rows.Add(deviceNames.GetValueOrDefault(variable.DeviceId), variable.Name, variable.Method, variable.DeviceAddress,
                EnumText(variable.DataType), EnumText(variable.EndianType), variable.Expressions, variable.Alias,
                variable.IsUpload, variable.Index, variable.IsTrigger, EnumText(variable.ProtectType), variable.Description);
        }

        await using var stream = new MemoryStream();
        await stream.SaveAsAsync(table, printHeader: true);
        return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = $"iotgateway_variables_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
        };
    }

    /// <summary>
    /// 导入变量 Excel（按设备名 + 变量名新增或更新）
    /// </summary>
    [DisplayName("导入变量"), ApiDescriptionSettings(Name = "Import"), HttpPost]
    public async Task<object> Import([FromForm] IFormFile file, [FromForm] long? deviceId)
    {
        if (file == null || file.Length == 0) throw Oops.Oh("请选择 Excel 文件");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw Oops.Oh("仅支持 .xlsx 文件");

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;
        List<Dictionary<string, object>> rows;
        try
        {
            var query = await stream.QueryAsync(useHeaderRow: true);
            rows = query.Cast<IDictionary<string, object>>()
                .Select(row => row.ToDictionary(u => u.Key, u => u.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            throw Oops.Oh($"读取 Excel 失败：{ex.Message}");
        }

        var devices = await _variableRep.Context.Queryable<IotDevice>().ToListAsync();
        var variables = await _variableRep.AsQueryable().ToListAsync();
        var affectedDeviceIds = new HashSet<long>();
        var insertCount = 0;
        var updateCount = 0;

        foreach (var row in rows)
        {
            var deviceName = Cell(row, "设备名");
            var name = Cell(row, "变量名");
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(deviceName)) continue;

            var targetDeviceId = !string.IsNullOrWhiteSpace(deviceName)
                ? devices.FirstOrDefault(u => u.DeviceName == deviceName)?.Id ?? deviceId
                : deviceId;
            if (targetDeviceId == null) throw Oops.Oh($"变量 {name} 所属设备不存在：{deviceName}");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var entity = variables.FirstOrDefault(u => u.DeviceId == targetDeviceId.Value && u.Name == name);
            if (entity == null)
            {
                entity = new IotDeviceVariable
                {
                    DeviceId = targetDeviceId.Value,
                    Name = name,
                    ProtectType = IotProtectTypeEnum.ReadOnly
                };
                ApplyRow(row, entity);
                await _variableRep.InsertAsync(entity);
                variables.Add(entity);
                insertCount++;
            }
            else
            {
                ApplyRow(row, entity);
                await _variableRep.AsUpdateable(entity).ExecuteCommandAsync();
                updateCount++;
            }
            affectedDeviceIds.Add(targetDeviceId.Value);
        }

        if (Runtime != null)
            foreach (var id in affectedDeviceIds)
                await Runtime.RestartDeviceAsync(id);

        return new { InsertCount = insertCount, UpdateCount = updateCount };
    }

    private static void ApplyRow(IDictionary<string, object> row, IotDeviceVariable entity)
    {
        var method = Cell(row, "方法");
        if (!string.IsNullOrWhiteSpace(method)) entity.Method = method;
        var address = Cell(row, "地址");
        if (!string.IsNullOrWhiteSpace(address)) entity.DeviceAddress = address;
        var dataType = Cell(row, "类型");
        if (!string.IsNullOrWhiteSpace(dataType)) entity.DataType = EnumValue(dataType, entity.DataType);
        var endian = Cell(row, "大小端");
        if (!string.IsNullOrWhiteSpace(endian)) entity.EndianType = EnumValue(endian, entity.EndianType);
        var expressions = Cell(row, "表达式");
        if (!string.IsNullOrWhiteSpace(expressions)) entity.Expressions = expressions;
        var alias = Cell(row, "别名");
        if (!string.IsNullOrWhiteSpace(alias)) entity.Alias = alias;
        var description = Cell(row, "描述");
        if (!string.IsNullOrWhiteSpace(description)) entity.Description = description;
        if (!string.IsNullOrWhiteSpace(Cell(row, "上传"))) entity.IsUpload = BoolCell(row, "上传");
        if (!string.IsNullOrWhiteSpace(Cell(row, "触发"))) entity.IsTrigger = BoolCell(row, "触发");
        if (!string.IsNullOrWhiteSpace(Cell(row, "排序"))) entity.Index = IntCell(row, "排序");
        var protect = Cell(row, "权限");
        if (!string.IsNullOrWhiteSpace(protect)) entity.ProtectType = EnumValue(protect, entity.ProtectType);
    }

    private static string Cell(IDictionary<string, object> row, string key)
    {
        return row.TryGetValue(key, out var value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : null;
    }

    private static int IntCell(IDictionary<string, object> row, string key, int defaultValue = 0)
    {
        var value = Cell(row, key);
        return int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) ? (int)number : defaultValue);
    }

    private static bool BoolCell(IDictionary<string, object> row, string key, bool defaultValue = false)
    {
        var value = Cell(row, key);
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        if (bool.TryParse(value, out var result)) return result;
        return value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "是";
    }

    private static string EnumText<T>(T value) where T : struct, Enum
    {
        var member = typeof(T).GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
    }

    private static T EnumValue<T>(string text, T defaultValue) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text)) return defaultValue;
        foreach (var value in Enum.GetValues<T>())
        {
            if (text.Equals(value.ToString(), StringComparison.OrdinalIgnoreCase) ||
                text.Equals(EnumText(value), StringComparison.OrdinalIgnoreCase) ||
                (int.TryParse(text, out var number) && number == Convert.ToInt32(value))) return value;
        }
        return defaultValue;
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
