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
    private IIotDriverProvider DriverProvider => _serviceProvider.GetService<IIotDriverProvider>();
    private IIotPlatformRuntime PlatformRuntime => _serviceProvider.GetService<IIotPlatformRuntime>();

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
        FillRuntimeValues(entity.DeviceVariables);
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
        if (input.DeviceTypeEnum == IotDeviceTypeEnum.Device && input.DriverId.HasValue)
            await AddDriverConfigsAsync(input.Id, input.DriverId.Value);
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
    /// 复制设备及其通讯配置、变量
    /// </summary>
    [DisplayName("复制设备"), ApiDescriptionSettings(Name = "Copy"), HttpPost]
    public async Task<List<long>> Copy([FromBody] IotDeviceCopyInput input)
    {
        var source = await _deviceRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("设备不存在");
        if (source.DeviceTypeEnum == IotDeviceTypeEnum.Group) throw Oops.Oh("分组不支持复制");

        var configs = await _configRep.AsQueryable().Where(u => u.DeviceId == input.Id).ToListAsync();
        var variables = await _variableRep.AsQueryable().Where(u => u.DeviceId == input.Id).OrderBy(u => u.Index).ToListAsync();
        var newIds = new List<long>();
        var db = _deviceRep.Context;
        db.Ado.BeginTran();
        try
        {
            for (var i = 1; i <= input.Count; i++)
            {
                var device = new IotDevice
                {
                    DeviceName = $"{source.DeviceName}-Copy{i}",
                    Index = source.Index,
                    Description = source.Description,
                    ParentId = source.ParentId,
                    DriverId = source.DriverId,
                    AutoStart = false,
                    CgUpload = source.CgUpload,
                    EnforcePeriod = source.EnforcePeriod,
                    CmdPeriod = source.CmdPeriod,
                    DeviceTypeEnum = source.DeviceTypeEnum
                };
                if (await _deviceRep.AsQueryable().AnyAsync(u => u.DeviceName == device.DeviceName))
                    throw Oops.Oh($"设备名称已存在：{device.DeviceName}");

                await _deviceRep.InsertAsync(device);
                newIds.Add(device.Id);

                foreach (var item in configs)
                {
                    await _configRep.InsertAsync(new IotDeviceConfig
                    {
                        DeviceId = device.Id,
                        DeviceConfigName = item.DeviceConfigName,
                        DataSide = item.DataSide,
                        Description = item.Description,
                        Value = item.Value,
                        EnumInfo = item.EnumInfo
                    });
                }

                foreach (var item in variables)
                {
                    await _variableRep.InsertAsync(new IotDeviceVariable
                    {
                        DeviceId = device.Id,
                        Name = item.Name,
                        Description = item.Description,
                        Method = item.Method,
                        DeviceAddress = item.DeviceAddress,
                        DataType = item.DataType,
                        IsTrigger = item.IsTrigger,
                        EndianType = item.EndianType,
                        Expressions = item.Expressions,
                        IsUpload = item.IsUpload,
                        ProtectType = item.ProtectType,
                        Index = item.Index,
                        Alias = item.Alias
                    });
                }
            }
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }

        if (Runtime != null)
            foreach (var id in newIds)
                await Runtime.RestartDeviceAsync(id);
        return newIds;
    }

    /// <summary>
    /// 请求设备共享属性
    /// </summary>
    [DisplayName("请求设备属性"), ApiDescriptionSettings(Name = "RequestAttributes"), HttpPost]
    public async Task RequestAttributes([FromBody] IotIdInput input)
    {
        var device = await _deviceRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("设备不存在");
        var keys = await _configRep.AsQueryable()
            .Where(u => u.DeviceId == input.Id && u.DataSide == IotDataSideEnum.AnySide)
            .Select(u => u.DeviceConfigName)
            .ToListAsync();
        if (keys.Count == 0) throw Oops.Oh("设备没有共享属性配置");
        if (PlatformRuntime == null) throw Oops.Oh("平台运行时未启动");
        await PlatformRuntime.RequestAttributesAsync(device.DeviceName, true, keys.ToArray());
    }

    /// <summary>
    /// 导出四 Sheet 完整备份
    /// </summary>
    [DisplayName("导出设备备份"), ApiDescriptionSettings(Name = "Export"), HttpGet]
    public async Task<IActionResult> Export()
    {
        var devices = await _deviceRep.AsQueryable().OrderBy(u => new { u.ParentId, u.Index, u.DeviceName }).ToListAsync();
        var drivers = await _driverRep.AsQueryable().ToListAsync();
        var configs = await _configRep.AsQueryable().OrderBy(u => new { u.DeviceId, u.DeviceConfigName }).ToListAsync();
        var variables = await _variableRep.AsQueryable().OrderBy(u => new { u.DeviceId, u.Alias, u.Method, u.Index, u.DeviceAddress }).ToListAsync();
        var systemConfig = await _deviceRep.Context.Queryable<IotSystemConfig>().FirstAsync();

        var sheets = new Dictionary<string, object>
        {
            { "设备维护", BuildDeviceTable(devices, drivers) },
            { "变量配置", BuildVariableTable(devices, variables) },
            { "通讯配置", BuildConfigTable(devices, configs) },
            { "传输配置", BuildSystemConfigTable(systemConfig) }
        };

        await using var stream = new MemoryStream();
        await stream.SaveAsAsync(sheets, printHeader: true);
        return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = $"iotgateway.net_bakup_{DateTime.Now:yyyyMMddHHmmssffff}.xlsx"
        };
    }

    /// <summary>
    /// 从四 Sheet 备份覆盖恢复设备业务数据
    /// </summary>
    [DisplayName("导入设备备份"), ApiDescriptionSettings(Name = "Import"), HttpPost]
    public async Task<object> Import([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0) throw Oops.Oh("请选择 Excel 备份文件");
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw Oops.Oh("仅支持 .xlsx 备份文件");

        var backup = await ParseBackupAsync(file);
        var db = _deviceRep.Context;
        var oldDeviceIds = await _deviceRep.AsQueryable().Select(u => u.Id).ToListAsync();
        if (Runtime != null)
            foreach (var id in oldDeviceIds)
                await Runtime.StopDeviceAsync(id);

        db.Ado.BeginTran();
        try
        {
            await db.Deleteable<IotRpcLog>().ExecuteCommandAsync();
            await db.Deleteable<IotDeviceConfig>().ExecuteCommandAsync();
            await db.Deleteable<IotDeviceVariable>().ExecuteCommandAsync();
            await db.Deleteable<IotDevice>().ExecuteCommandAsync();

            var deviceIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in backup.Devices.OrderBy(u => u.Entity.DeviceTypeEnum == IotDeviceTypeEnum.Group ? 0 : 1))
            {
                var entity = row.Entity;
                entity.Id = 0;
                if (string.IsNullOrWhiteSpace(row.ParentName))
                    entity.ParentId = null;
                else if (deviceIds.TryGetValue(row.ParentName, out var parentId))
                    entity.ParentId = parentId;
                else
                    throw Oops.Oh($"设备 {entity.DeviceName} 所属分组不存在：{row.ParentName}");
                if (entity.DeviceTypeEnum == IotDeviceTypeEnum.Device && !string.IsNullOrWhiteSpace(row.DriverName))
                {
                    var driver = backup.Drivers.FirstOrDefault(u => u.DriverName == row.DriverName)
                        ?? throw Oops.Oh($"设备 {entity.DeviceName} 的驱动不存在：{row.DriverName}");
                    entity.DriverId = driver.Id;
                }
                await _deviceRep.InsertAsync(entity);
                deviceIds[entity.DeviceName] = entity.Id;
            }

            foreach (var row in backup.Variables)
            {
                if (!deviceIds.TryGetValue(row.DeviceName, out var deviceId))
                    throw Oops.Oh($"变量 {row.Entity.Name} 所属设备不存在：{row.DeviceName}");
                row.Entity.Id = 0;
                row.Entity.DeviceId = deviceId;
                await _variableRep.InsertAsync(row.Entity);
            }

            foreach (var row in backup.Configs)
            {
                if (!deviceIds.TryGetValue(row.DeviceName, out var deviceId))
                    throw Oops.Oh($"通讯配置 {row.Entity.DeviceConfigName} 所属设备不存在：{row.DeviceName}");
                row.Entity.Id = 0;
                row.Entity.DeviceId = deviceId;
                await _configRep.InsertAsync(row.Entity);
            }

            var systemConfig = await db.Queryable<IotSystemConfig>().FirstAsync();
            if (systemConfig == null)
            {
                systemConfig = backup.SystemConfig;
                await db.Insertable(systemConfig).ExecuteCommandAsync();
            }
            else
            {
                systemConfig.GatewayName = backup.SystemConfig.GatewayName;
                systemConfig.ClientId = backup.SystemConfig.ClientId;
                systemConfig.IoTPlatformType = backup.SystemConfig.IoTPlatformType;
                systemConfig.MqttIp = backup.SystemConfig.MqttIp;
                systemConfig.MqttPort = backup.SystemConfig.MqttPort;
                systemConfig.MqttUName = backup.SystemConfig.MqttUName;
                systemConfig.MqttUPwd = backup.SystemConfig.MqttUPwd;
                await db.Updateable(systemConfig).ExecuteCommandAsync();
            }

            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            if (Runtime != null) await Runtime.ReloadAsync();
            throw;
        }

        if (Runtime != null) await Runtime.ReloadAsync();
        if (PlatformRuntime != null) await PlatformRuntime.ReloadAsync();
        return new
        {
            DeviceCount = backup.Devices.Count(u => u.Entity.DeviceTypeEnum == IotDeviceTypeEnum.Device),
            GroupCount = backup.Devices.Count(u => u.Entity.DeviceTypeEnum == IotDeviceTypeEnum.Group),
            VariableCount = backup.Variables.Count,
            ConfigCount = backup.Configs.Count
        };
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

    private static DataTable BuildDeviceTable(List<IotDevice> devices, List<IotDriver> drivers)
    {
        var table = CreateTable("设备维护", "名称", "排序", "驱动名", "启动", "变化上传", "归档周期ms", "指令间隔ms", "类型", "所属组");
        foreach (var device in devices)
        {
            table.Rows.Add(device.DeviceName, device.Index, drivers.FirstOrDefault(u => u.Id == device.DriverId)?.DriverName,
                device.AutoStart, device.CgUpload, device.EnforcePeriod, device.CmdPeriod, EnumText(device.DeviceTypeEnum),
                devices.FirstOrDefault(u => u.Id == device.ParentId)?.DeviceName);
        }
        return table;
    }

    private static DataTable BuildVariableTable(List<IotDevice> devices, List<IotDeviceVariable> variables)
    {
        var table = CreateTable("变量配置", "设备名", "变量名", "方法", "地址", "类型", "大小端", "表达式", "别名", "上传", "排序", "触发");
        var deviceNames = devices.ToDictionary(u => u.Id, u => u.DeviceName);
        foreach (var variable in variables)
        {
            table.Rows.Add(deviceNames.GetValueOrDefault(variable.DeviceId), variable.Name, variable.Method, variable.DeviceAddress,
                EnumText(variable.DataType), EnumText(variable.EndianType), variable.Expressions, variable.Alias,
                variable.IsUpload, variable.Index, variable.IsTrigger);
        }
        return table;
    }

    private static DataTable BuildConfigTable(List<IotDevice> devices, List<IotDeviceConfig> configs)
    {
        var table = CreateTable("通讯配置", "设备名", "名称", "属性侧", "描述", "值", "备注");
        var deviceNames = devices.ToDictionary(u => u.Id, u => u.DeviceName);
        foreach (var config in configs)
        {
            table.Rows.Add(deviceNames.GetValueOrDefault(config.DeviceId), config.DeviceConfigName,
                config.DataSide == IotDataSideEnum.AnySide ? "共享属性" : "客户端", config.Description, config.Value, config.EnumInfo);
        }
        return table;
    }

    private static DataTable BuildSystemConfigTable(IotSystemConfig config)
    {
        var table = CreateTable("传输配置", "网关名称", "ClientId", "输出平台", "Mqtt服务器", "Mqtt端口", "Mqtt用户名", "Mqtt密码");
        if (config != null)
            table.Rows.Add(config.GatewayName, config.ClientId, EnumText(config.IoTPlatformType), config.MqttIp,
                config.MqttPort, config.MqttUName, config.MqttUPwd);
        return table;
    }

    private static DataTable CreateTable(string name, params string[] columns)
    {
        var table = new DataTable(name);
        foreach (var column in columns) table.Columns.Add(column);
        return table;
    }

    private static string EnumText<T>(T value) where T : struct, Enum
    {
        var member = typeof(T).GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
    }

    private async Task<BackupData> ParseBackupAsync(IFormFile file)
    {
        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        var deviceRows = await ReadSheetAsync(stream, "设备维护");
        var variableRows = await ReadSheetAsync(stream, "变量配置");
        var configRows = await ReadSheetAsync(stream, "通讯配置");
        var systemRows = await ReadSheetAsync(stream, "传输配置");
        var backup = new BackupData { Drivers = await _driverRep.AsQueryable().ToListAsync() };

        foreach (var row in deviceRows)
        {
            var name = Cell(row, "名称");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var parentName = Cell(row, "所属组");
            var typeText = Cell(row, "类型");
            var type = string.IsNullOrWhiteSpace(typeText)
                ? (string.IsNullOrWhiteSpace(parentName) ? IotDeviceTypeEnum.Group : IotDeviceTypeEnum.Device)
                : EnumValue(typeText, IotDeviceTypeEnum.Device);
            backup.Devices.Add(new DeviceImportRow
            {
                ParentName = parentName,
                DriverName = Cell(row, "驱动名"),
                Entity = new IotDevice
                {
                    DeviceName = name,
                    Index = IntCell(row, "排序"),
                    AutoStart = BoolCell(row, "启动"),
                    CgUpload = BoolCell(row, "变化上传"),
                    EnforcePeriod = IntCell(row, "归档周期ms"),
                    CmdPeriod = IntCell(row, "指令间隔ms"),
                    DeviceTypeEnum = type
                }
            });
        }
        if (backup.Devices.GroupBy(u => u.Entity.DeviceName, StringComparer.OrdinalIgnoreCase).Any(u => u.Count() > 1))
            throw Oops.Oh("设备维护 Sheet 中存在重复设备名称");

        foreach (var row in variableRows)
        {
            var deviceName = Cell(row, "设备名");
            var name = Cell(row, "变量名");
            if (string.IsNullOrWhiteSpace(deviceName) && string.IsNullOrWhiteSpace(name)) continue;
            backup.Variables.Add(new VariableImportRow
            {
                DeviceName = deviceName,
                Entity = new IotDeviceVariable
                {
                    Name = name,
                    Method = Cell(row, "方法"),
                    DeviceAddress = Cell(row, "地址"),
                    DataType = EnumValue(Cell(row, "类型"), IotDataTypeEnum.Any),
                    EndianType = EnumValue(Cell(row, "大小端"), IotEndianEnum.None),
                    Expressions = Cell(row, "表达式"),
                    Alias = Cell(row, "别名"),
                    IsUpload = BoolCell(row, "上传", true),
                    Index = IntCell(row, "排序", 999),
                    IsTrigger = BoolCell(row, "触发", true),
                    ProtectType = IotProtectTypeEnum.ReadOnly
                }
            });
        }

        foreach (var row in configRows)
        {
            var deviceName = Cell(row, "设备名");
            var name = Cell(row, "名称");
            if (string.IsNullOrWhiteSpace(deviceName) && string.IsNullOrWhiteSpace(name)) continue;
            var side = Cell(row, "属性侧");
            backup.Configs.Add(new ConfigImportRow
            {
                DeviceName = deviceName,
                Entity = new IotDeviceConfig
                {
                    DeviceConfigName = name,
                    DataSide = side == "共享属性" || side == "任意侧" || side == "AnySide" || side == "0"
                        ? IotDataSideEnum.AnySide
                        : IotDataSideEnum.ClientSide,
                    Description = Cell(row, "描述"),
                    Value = Cell(row, "值"),
                    EnumInfo = Cell(row, "备注")
                }
            });
        }

        var systemRow = systemRows.FirstOrDefault(u => !string.IsNullOrWhiteSpace(Cell(u, "网关名称")))
            ?? throw Oops.Oh("传输配置 Sheet 没有有效数据");
        backup.SystemConfig = new IotSystemConfig
        {
            GatewayName = Cell(systemRow, "网关名称"),
            ClientId = Cell(systemRow, "ClientId"),
            IoTPlatformType = EnumValue(Cell(systemRow, "输出平台"), IotPlatformTypeEnum.IoTSharp),
            MqttIp = Cell(systemRow, "Mqtt服务器"),
            MqttPort = IntCell(systemRow, "Mqtt端口", 1888),
            MqttUName = Cell(systemRow, "Mqtt用户名"),
            MqttUPwd = Cell(systemRow, "Mqtt密码")
        };
        return backup;
    }

    private static async Task<List<Dictionary<string, object>>> ReadSheetAsync(MemoryStream stream, string sheetName)
    {
        stream.Position = 0;
        try
        {
            var rows = await stream.QueryAsync(sheetName: sheetName, useHeaderRow: true);
            return rows.Cast<IDictionary<string, object>>()
                .Select(row => row.ToDictionary(u => u.Key, u => u.Value))
                .ToList();
        }
        catch (Exception ex)
        {
            throw Oops.Oh($"读取 {sheetName} Sheet 失败，请使用四 Sheet 备份文件。{ex.Message}");
        }
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

    private sealed class BackupData
    {
        public List<IotDriver> Drivers { get; set; } = new();
        public List<DeviceImportRow> Devices { get; set; } = new();
        public List<VariableImportRow> Variables { get; set; } = new();
        public List<ConfigImportRow> Configs { get; set; } = new();
        public IotSystemConfig SystemConfig { get; set; }
    }

    private sealed class DeviceImportRow
    {
        public IotDevice Entity { get; set; }
        public string ParentName { get; set; }
        public string DriverName { get; set; }
    }

    private sealed class VariableImportRow
    {
        public IotDeviceVariable Entity { get; set; }
        public string DeviceName { get; set; }
    }

    private sealed class ConfigImportRow
    {
        public IotDeviceConfig Entity { get; set; }
        public string DeviceName { get; set; }
    }

    private async Task DeleteChildrenAsync(List<long> deviceIds)
    {
        await _configRep.Context.Deleteable<IotDeviceConfig>().In(u => u.DeviceId, deviceIds).ExecuteCommandAsync();
        await _variableRep.Context.Deleteable<IotDeviceVariable>().In(u => u.DeviceId, deviceIds).ExecuteCommandAsync();
        await _configRep.Context.Deleteable<IotRpcLog>().In(u => u.DeviceId, deviceIds).ExecuteCommandAsync();
    }

    private async Task AddDriverConfigsAsync(long deviceId, long driverId)
    {
        var driver = await _driverRep.GetByIdAsync(driverId) ?? throw Oops.Oh("驱动不存在");
        var configs = DriverProvider?.GetDriverConfigs(driver.AssembleName) ?? new List<IotDriverConfigDto>();
        foreach (var item in configs)
        {
            await _configRep.InsertAsync(new IotDeviceConfig
            {
                DeviceId = deviceId,
                DeviceConfigName = item.Name,
                DataSide = IotDataSideEnum.AnySide,
                Description = item.Description,
                Value = item.DefaultValue,
                EnumInfo = item.EnumInfo
            });
        }
    }

    private void FillRuntimeValues(List<IotDeviceVariable> variables)
    {
        if (Runtime == null || variables.Count == 0) return;
        var runtimeVariables = Runtime.GetDeviceVariables(variables[0].DeviceId);
        foreach (var variable in variables)
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
