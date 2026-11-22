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
    private readonly ILogger<IotDeviceRuntimeService> _logger;

    private readonly ConcurrentDictionary<long, IotDeviceThread> _threads = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IotDeviceRuntimeService(
        ISqlSugarClient db,
        IotDriverManager driverManager,
        IotMqttServer mqttServer,
        IotPlatformService platformService,
        ILogger<IotDeviceRuntimeService> logger)
    {
        _db = db;
        _driverManager = driverManager;
        _mqttServer = mqttServer;
        _platformService = platformService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 等待 MQTT Broker 与数据库初始化完成
            await Task.Delay(3000, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ReloadAsync();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        StopAll();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 全量重载
    /// </summary>
    public async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            StopAll();

            var devices = await _db.Queryable<IotDevice>()
                .Where(u => u.DeviceTypeEnum == IotDeviceTypeEnum.Device)
                .ToListAsync();

            _logger.LogInformation("加载设备数量：{Count}", devices.Count);

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

        var thread = new IotDeviceThread(device, driver, _mqttServer, _platformService, _logger);
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
