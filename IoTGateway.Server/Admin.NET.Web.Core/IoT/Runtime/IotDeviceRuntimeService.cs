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
    private readonly ILogger<IotDeviceRuntimeService> _logger;

    private readonly ConcurrentDictionary<long, IotDeviceThread> _threads = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IotDeviceRuntimeService(
        ISqlSugarClient db,
        IotDriverManager driverManager,
        IotMqttServer mqttServer,
        ILogger<IotDeviceRuntimeService> logger)
    {
        _db = db;
        _driverManager = driverManager;
        _mqttServer = mqttServer;
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

        var thread = new IotDeviceThread(device, driver, _mqttServer, _logger);
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
