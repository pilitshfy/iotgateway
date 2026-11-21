namespace Admin.NET.Application;

/// <summary>
/// 设备采集运行时（由 Web.Core 实现），用于在设备/配置/变量变更后刷新采集线程
/// </summary>
public interface IIotDeviceRuntime
{
    /// <summary>
    /// 全量重载设备采集线程
    /// </summary>
    Task ReloadAsync();

    /// <summary>
    /// 重启指定设备的采集线程
    /// </summary>
    Task RestartDeviceAsync(long deviceId);

    /// <summary>
    /// 停止指定设备的采集线程
    /// </summary>
    Task StopDeviceAsync(long deviceId);
}
