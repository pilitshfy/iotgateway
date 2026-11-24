using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PluginInterface;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 设备采集运行时：加载驱动、设备、通讯配置与变量，并维护采集线程
/// </summary>
public class IotDeviceRuntimeService : BackgroundService, IIotDeviceRuntime
{
    private readonly ISqlSugarClient _db;
    private readonly IotDriverManager _driverManager;
    private readonly IotMqttServer _mqttServer;
    private readonly IotPlatformService _platformService;
    private readonly IIotHistorySink _historySink;
    private readonly IIotAlarmSink _alarmSink;
    private readonly IotCollectorOptions _collector;
    private readonly ILogger<IotDeviceRuntimeService> _logger;

    private readonly ConcurrentDictionary<long, IotDeviceThread> _threads = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastSyncStamp = DateTime.MinValue;

    public IotDeviceRuntimeService(
        ISqlSugarClient db,
        IotDriverManager driverManager,
        IotMqttServer mqttServer,
        IotPlatformService platformService,
        IIotHistorySink historySink,
        IIotAlarmSink alarmSink,
        IOptions<IotCollectorOptions> collectorOptions,
        ILogger<IotDeviceRuntimeService> logger)
    {
        _db = db;
        _driverManager = driverManager;
        _mqttServer = mqttServer;
        _platformService = platformService;
        _historySink = historySink;
        _alarmSink = alarmSink;
        _collector = collectorOptions.Value;
        _logger = logger;

        if (!_collector.Normalize())
            _logger.LogWarning("采集分片配置无效（ShardIndex={ShardIndex}, ShardCount={ShardCount}），已回退为单分片",
                _collector.ShardIndex, _collector.ShardCount);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_collector.Enabled)
        {
            _logger.LogInformation("采集运行时已禁用（IotCollector:Enabled=false），本实例不加载设备");
            return;
        }

        try
        {
            // 等待 MQTT Broker 与数据库初始化完成
            await Task.Delay(3000, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("采集分片 {ShardIndex}/{ShardCount} 启动，配置同步间隔 {SyncSeconds}s",
            _collector.ShardIndex, _collector.ShardCount, _collector.SyncSeconds);

        await ReloadAsync();

        if (_collector.SyncSeconds <= 0) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_collector.SyncSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SyncAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集配置同步循环异常退出");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopAll();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 全量重载（仅本分片负责的设备）
    /// </summary>
    public async Task ReloadAsync()
    {
        if (!_collector.Enabled) return;

        await _gate.WaitAsync();
        try
        {
            StopAll();
            _lastSyncStamp = await GetMaxStampAsync();

            var all = await _db.Queryable<IotDevice>()
                .Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device)
                .ToListAsync();
            var devices = all.Where(u => OwnsDevice(u.Id)).ToList();

            _logger.LogInformation("采集分片 {ShardIndex}/{ShardCount}：加载设备 {Count} 台（全量 {Total} 台）",
                _collector.ShardIndex, _collector.ShardCount, devices.Count, all.Count);

            foreach (var device in devices)
            {
                await BuildAndStartAsync(device.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备采集运行时加载失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 配置同步：对齐本分片设备集合，并重启配置发生变更的设备。
    /// 多实例部署时由各实例独立轮询配置库，避免依赖进程内通知。
    /// </summary>
    private async Task SyncAsync()
    {
        await _gate.WaitAsync();
        try
        {
            // 先取时间戳快照，同步期间产生的新变更留到下一轮
            var stamp = await GetMaxStampAsync();

            var all = await _db.Queryable<IotDevice>()
                .Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device)
                .ToListAsync();
            var owned = all.Where(u => OwnsDevice(u.Id)).ToDictionary(u => u.Id);

            // 已删除、停用或重新分片到其他实例的设备
            foreach (var deviceId in _threads.Keys.ToList())
            {
                if (!owned.ContainsKey(deviceId)) StopDevice(deviceId);
            }

            // 新增或重新分片到本实例的设备
            foreach (var deviceId in owned.Keys)
            {
                if (!_threads.ContainsKey(deviceId)) await BuildAndStartAsync(deviceId);
            }

            // 设备/变量/通讯配置发生变更的设备
            var changed = await GetChangedDeviceIdsAsync(_lastSyncStamp);
            var restarted = 0;
            foreach (var deviceId in changed)
            {
                if (!owned.ContainsKey(deviceId) || !_threads.ContainsKey(deviceId)) continue;
                StopDevice(deviceId);
                await BuildAndStartAsync(deviceId);
                restarted++;
            }

            if (restarted > 0)
                _logger.LogInformation("采集分片 {ShardIndex}/{ShardCount}：配置变更，重启设备 {Count} 台",
                    _collector.ShardIndex, _collector.ShardCount, restarted);

            _lastSyncStamp = stamp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "采集配置同步失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 指定设备是否由当前实例采集
    /// </summary>
    private bool OwnsDevice(long deviceId)
    {
        if (_collector.ShardCount <= 1) return true;
        return (int)(deviceId % _collector.ShardCount) == _collector.ShardIndex;
    }

    /// <summary>
    /// 配置库中设备/变量/通讯配置的最大变更时间
    /// </summary>
    private async Task<DateTime> GetMaxStampAsync()
    {
        List<DateTime?> stamps = new()
        {
            await _db.Queryable<IotDevice>().MaxAsync(u => u.UpdateTime),
            await _db.Queryable<IotDevice>().MaxAsync(u => u.CreateTime),
            await _db.Queryable<IotDeviceVariable>().MaxAsync(u => u.UpdateTime),
            await _db.Queryable<IotDeviceVariable>().MaxAsync(u => u.CreateTime),
            await _db.Queryable<IotDeviceConfig>().MaxAsync(u => u.UpdateTime),
            await _db.Queryable<IotDeviceConfig>().MaxAsync(u => u.CreateTime)
        };

        return stamps.Where(u => u.HasValue).Select(u => u.Value).DefaultIfEmpty(DateTime.MinValue).Max();
    }

    /// <summary>
    /// 自指定时间以来配置发生变更的设备Id
    /// </summary>
    private async Task<HashSet<long>> GetChangedDeviceIdsAsync(DateTime since)
    {
        var ids = new HashSet<long>();
        if (since == DateTime.MinValue) return ids;

        var deviceIds = await _db.Queryable<IotDevice>()
            .Where(u => u.UpdateTime > since || u.CreateTime > since)
            .Select(u => u.Id).ToListAsync();
        var variableDeviceIds = await _db.Queryable<IotDeviceVariable>()
            .Where(u => u.UpdateTime > since || u.CreateTime > since)
            .Select(u => u.DeviceId).ToListAsync();
        var configDeviceIds = await _db.Queryable<IotDeviceConfig>()
            .Where(u => u.UpdateTime > since || u.CreateTime > since)
            .Select(u => u.DeviceId).ToListAsync();

        foreach (var id in deviceIds) ids.Add(id);
        foreach (var id in variableDeviceIds) ids.Add(id);
        foreach (var id in configDeviceIds) ids.Add(id);

        return ids;
    }

    /// <summary>
    /// 重启指定设备
    /// </summary>
    public async Task RestartDeviceAsync(long deviceId)
    {
        await _gate.WaitAsync();
        try
        {
            StopDevice(deviceId);
            await BuildAndStartAsync(deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重启设备采集线程失败：{DeviceId}", deviceId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 停止指定设备
    /// </summary>
    public Task StopDeviceAsync(long deviceId)
    {
        StopDevice(deviceId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 正在运行的设备Id
    /// </summary>
    public List<long> GetRunningDeviceIds()
    {
        return _threads.Where(x => x.Value.Device.AutoStart && x.Value.Driver.IsConnected)
            .Select(x => x.Key).ToList();
    }

    /// <summary>
    /// 获取采集线程中的实时变量
    /// </summary>
    public List<IotDeviceVariable> GetDeviceVariables(long deviceId)
    {
        return _threads.TryGetValue(deviceId, out var thread)
            ? thread.Device.DeviceVariables
            : new List<IotDeviceVariable>();
    }

    /// <summary>
    /// 执行 RPC（写入设备变量）
    /// </summary>
    public async Task<IotRpcResultDto> ExecuteRpcAsync(IotRpcRequestDto request)
    {
        if (request?.Params != null)
            request.Params = request.Params.ToDictionary(u => u.Key, u => NormalizeRpcValue(u.Value));

        var result = new IotRpcResultDto
        {
            DeviceName = request?.DeviceName,
            Method = request?.Method,
            RequestId = request?.RequestId,
            IsSuccess = false
        };

        var thread = _threads.Values.FirstOrDefault(x =>
            x.Device.DeviceName == request?.DeviceName ||
            (x.Device.DeviceVariables != null && x.Device.DeviceVariables.Any(v => v.Alias == request.DeviceName)));

        var startTime = DateTime.Now;
        try
        {
            if (thread == null)
            {
                result.Description = $"未找到设备：{request?.DeviceName}";
            }
            else
            {
                var response = await thread.ExecuteRpcAsync(request.DeviceName, request.Method, request.RequestId, request.Params);
                result.IsSuccess = response.IsSuccess;
                result.Description = response.Description;
            }
        }
        catch (Exception ex)
        {
            result.Description = ex.Message;
            _logger.LogError(ex, "执行 RPC 失败：{Device}/{Method}", request?.DeviceName, request?.Method);
        }

        try
        {
            await _db.Insertable(new IotRpcLog
            {
                DeviceId = thread?.Device.Id,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Method = request?.Method,
                RpcSide = IotRpcSideEnum.ServerSide,
                Params = request?.Params == null ? null : Newtonsoft.Json.JsonConvert.SerializeObject(request.Params),
                IsSuccess = result.IsSuccess,
                Description = result.Description
            }).ExecuteCommandAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录 RPC 日志失败");
        }

        return result;
    }

    /// <summary>
    /// 采集线程中的设备名称
    /// </summary>
    public List<string> GetDeviceNames()
    {
        return _threads.Values.Select(x => x.Device.DeviceName).ToList();
    }

    /// <summary>
    /// 设备连接状态（未加载返回 null）
    /// </summary>
    public bool? GetDeviceStatus(string deviceName)
    {
        return FindThread(deviceName)?.Driver.IsConnected;
    }

    /// <summary>
    /// 连接设备
    /// </summary>
    public bool ConnectDevice(string deviceName)
    {
        return FindThread(deviceName)?.Driver.Connect() ?? false;
    }

    /// <summary>
    /// 断开设备
    /// </summary>
    public bool DisconnectDevice(string deviceName)
    {
        return FindThread(deviceName)?.Driver.Close() ?? false;
    }

    /// <summary>
    /// 按设备名称重启采集线程
    /// </summary>
    public async Task<bool> RestartDeviceByNameAsync(string deviceName)
    {
        var pair = _threads.FirstOrDefault(x => x.Value.Device.DeviceName == deviceName);
        if (pair.Value == null) return false;

        await RestartDeviceAsync(pair.Key);
        return true;
    }

    /// <summary>
    /// 设备详细信息
    /// </summary>
    public Dictionary<string, object> GetDeviceInfo(string deviceName)
    {
        var thread = FindThread(deviceName);
        if (thread == null)
            return new Dictionary<string, object> { { "error", "Device not found" } };

        var device = thread.Device;
        var info = new Dictionary<string, object>
        {
            { "DeviceName", device.DeviceName },
            { "Description", device.Description ?? string.Empty },
            { "AutoStart", device.AutoStart },
            { "ChangeUpload", device.CgUpload },
            { "EnforcePeriod", device.EnforcePeriod },
            { "CmdPeriod", device.CmdPeriod },
            { "DeviceType", device.DeviceTypeEnum.ToString() },
            { "IsConnected", thread.Driver.IsConnected },
            { "Timeout", thread.Driver.Timeout },
            { "MinPeriod", thread.Driver.MinPeriod },
            { "VariableCount", device.DeviceVariables?.Count ?? 0 },
            { "ConfigCount", device.DeviceConfigs?.Count ?? 0 }
        };

        if (device.DeviceConfigs != null)
            info["Configs"] = device.DeviceConfigs.ToDictionary(x => x.DeviceConfigName, x => x.Value);

        return info;
    }

    /// <summary>
    /// 按设备名称获取实时变量
    /// </summary>
    public List<IotDeviceVariable> GetDeviceVariablesByName(string deviceName)
    {
        return FindThread(deviceName)?.Device.DeviceVariables ?? new List<IotDeviceVariable>();
    }

    /// <summary>
    /// 获取设备驱动名称
    /// </summary>
    public async Task<string> GetDeviceDriverNameAsync(string deviceName)
    {
        var thread = FindThread(deviceName);
        if (thread?.Device.DriverId == null) return string.Empty;

        var driver = await _db.Queryable<IotDriver>().InSingleAsync(thread.Device.DriverId.Value);
        return driver?.DriverName ?? string.Empty;
    }

    private IotDeviceThread FindThread(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;
        return _threads.Values.FirstOrDefault(x => x.Device.DeviceName == deviceName);
    }

    private static object NormalizeRpcValue(object value)
    {
        if (value is not System.Text.Json.JsonElement element) return value;
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                return element.EnumerateObject().ToDictionary(u => u.Name, u => NormalizeRpcValue(u.Value));
            case System.Text.Json.JsonValueKind.Array:
                return element.EnumerateArray().Select(u => NormalizeRpcValue(u)).ToList();
            case System.Text.Json.JsonValueKind.String:
                return element.GetString();
            case System.Text.Json.JsonValueKind.Number:
                if (element.TryGetInt64(out var integer)) return integer;
                if (element.TryGetDecimal(out var decimalValue)) return decimalValue;
                return element.GetDouble();
            case System.Text.Json.JsonValueKind.True:
                return true;
            case System.Text.Json.JsonValueKind.False:
                return false;
            case System.Text.Json.JsonValueKind.Null:
            case System.Text.Json.JsonValueKind.Undefined:
                return null;
            default:
                return element.ToString();
        }
    }

    private async Task BuildAndStartAsync(long deviceId)
    {
        // API/Web 实例不采集；非本分片设备也不采集
        if (!_collector.Enabled || !OwnsDevice(deviceId)) return;

        var device = await _db.Queryable<IotDevice>().InSingleAsync(deviceId);
        if (device == null || device.DeviceTypeEnum != IotDeviceTypeEnum.Device || device.DriverId == null)
            return;

        var driverEntity = await _db.Queryable<IotDriver>().InSingleAsync(device.DriverId.Value);
        if (driverEntity == null || string.IsNullOrWhiteSpace(driverEntity.AssembleName))
        {
            _logger.LogWarning("设备 {DeviceName} 未配置有效驱动", device.DeviceName);
            return;
        }

        var configs = await _db.Queryable<IotDeviceConfig>().Where(u => u.DeviceId == deviceId).ToListAsync();
        var variables = await _db.Queryable<IotDeviceVariable>().Where(u => u.DeviceId == deviceId).OrderBy(u => u.Index).ToListAsync();

        device.DeviceConfigs = configs;
        device.DeviceVariables = variables;

        var driver = _driverManager.CreateDriver(device.DeviceName, driverEntity.AssembleName);
        if (driver == null)
        {
            _logger.LogWarning("设备 {DeviceName} 驱动创建失败：{AssembleName}", device.DeviceName, driverEntity.AssembleName);
            return;
        }

        ApplyConfigs(driver, configs);

        var thread = new IotDeviceThread(device, driver, _mqttServer, _platformService, _logger, _historySink, _alarmSink);
        _threads[deviceId] = thread;
        thread.Start();

        _logger.LogInformation("设备已加载：{DeviceName}，变量 {Count} 个", device.DeviceName, variables.Count);
    }

    private void ApplyConfigs(IDriver driver, List<IotDeviceConfig> configs)
    {
        foreach (var property in driver.GetType().GetProperties())
        {
            if (property.GetCustomAttribute<ConfigParameterAttribute>() == null)
                continue;

            var setting = configs.FirstOrDefault(x => x.DeviceConfigName == property.Name);
            if (setting == null)
                continue;

            try
            {
                property.SetValue(driver, ConvertSettingValue(property.PropertyType, setting.Value));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置驱动参数失败：{Driver}/{Property}", driver.GetType().Name, property.Name);
            }
        }
    }

    private static object ConvertSettingValue(Type targetType, string value)
    {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(bool)) return value != "0" && !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        if (targetType == typeof(IPAddress)) return IPAddress.Parse(value);
        if (targetType.IsEnum) return Enum.Parse(targetType, value);
        if (targetType == typeof(Guid)) return string.IsNullOrWhiteSpace(value) ? Guid.Empty : Guid.Parse(value);
        return Convert.ChangeType(value, targetType);
    }

    private void StopDevice(long deviceId)
    {
        if (!_threads.TryRemove(deviceId, out var thread)) return;

        thread.StopThread();
        thread.Dispose();
        _logger.LogInformation("设备采集线程已移除：{DeviceId}", deviceId);
    }

    private void StopAll()
    {
        foreach (var deviceId in _threads.Keys.ToList())
        {
            StopDevice(deviceId);
        }
    }
}
