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

    /// <summary>
    /// 正在运行的设备Id
    /// </summary>
    List<long> GetRunningDeviceIds();

    /// <summary>
    /// 执行 RPC（写入设备变量）
    /// </summary>
    Task<IotRpcResultDto> ExecuteRpcAsync(IotRpcRequestDto request);
}

/// <summary>
/// RPC 请求
/// </summary>
public class IotRpcRequestDto
{
    public string DeviceName { get; set; }

    public string Method { get; set; }

    public string RequestId { get; set; }

    public Dictionary<string, object> Params { get; set; }
}

/// <summary>
/// RPC 结果
/// </summary>
public class IotRpcResultDto
{
    public string DeviceName { get; set; }

    public string Method { get; set; }

    public string RequestId { get; set; }

    public bool IsSuccess { get; set; }

    public string Description { get; set; }
}

/// <summary>
/// 上游平台运行控制（由 Web.Core 实现）
/// </summary>
public interface IIotPlatformRuntime
{
    /// <summary>
    /// 重新加载平台连接与处理器
    /// </summary>
    Task ReloadAsync();
}
