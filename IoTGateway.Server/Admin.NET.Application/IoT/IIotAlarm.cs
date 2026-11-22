namespace Admin.NET.Application;

/// <summary>
/// 实时采集样本，由采集线程非阻塞投递到告警引擎
/// </summary>
public sealed class IotAlarmSample
{
    public long DeviceId { get; init; }
    public string DeviceName { get; init; }
    public long OrgId { get; init; }
    public long VariableId { get; init; }
    public string VariableName { get; init; }
    public object Value { get; init; }
    public IotVariableStatusEnum Status { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 告警样本入口
/// </summary>
public interface IIotAlarmSink
{
    bool Enabled { get; }
    void Enqueue(IotAlarmSample sample);
}

/// <summary>
/// 告警运行时规则重载
/// </summary>
public interface IIotAlarmRuntime
{
    Task ReloadAsync();
}

/// <summary>
/// 告警通知发送结果
/// </summary>
public sealed class IotAlarmNotificationResult
{
    public bool IsSuccess { get; init; }
    public string Content { get; init; }
    public string Response { get; init; }
}

/// <summary>
/// 告警通知发送器，由 Web.Core 实现
/// </summary>
public interface IIotAlarmNotifier
{
    Task<IotAlarmNotificationResult> SendAsync(
        IotAlarmChannel channel,
        IotAlarmEvent alarmEvent,
        IotAlarmNotificationTypeEnum notificationType,
        CancellationToken cancellationToken = default);
}
