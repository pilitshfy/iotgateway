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
    /// 获取采集线程中的实时变量
    /// </summary>
    List<IotDeviceVariable> GetDeviceVariables(long deviceId);

    /// <summary>
    /// 执行 RPC（写入设备变量）
    /// </summary>
    Task<IotRpcResultDto> ExecuteRpcAsync(IotRpcRequestDto request);

    /// <summary>
    /// 采集线程中的设备名称
    /// </summary>
    List<string> GetDeviceNames();

    /// <summary>
    /// 设备连接状态（未加载返回 null）
    /// </summary>
    bool? GetDeviceStatus(string deviceName);

    /// <summary>
    /// 连接设备
    /// </summary>
    bool ConnectDevice(string deviceName);

    /// <summary>
    /// 断开设备
    /// </summary>
    bool DisconnectDevice(string deviceName);

    /// <summary>
    /// 按设备名称重启采集线程
    /// </summary>
    Task<bool> RestartDeviceByNameAsync(string deviceName);

    /// <summary>
    /// 设备详细信息
    /// </summary>
    Dictionary<string, object> GetDeviceInfo(string deviceName);

    /// <summary>
    /// 按设备名称获取实时变量
    /// </summary>
    List<IotDeviceVariable> GetDeviceVariablesByName(string deviceName);

    /// <summary>
    /// 获取设备驱动名称
    /// </summary>
    Task<string> GetDeviceDriverNameAsync(string deviceName);
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

    /// <summary>
    /// 上传设备客户端属性
    /// </summary>
    Task UploadAttributesAsync(string deviceName, object attributes);

    /// <summary>
    /// 请求设备属性
    /// </summary>
    Task RequestAttributesAsync(string deviceName, bool anySide, params string[] keys);
}
