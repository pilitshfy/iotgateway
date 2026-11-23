using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Admin.NET.Application;

/// <summary>
/// 时序历史写入器（由 Web.Core 实现，内部批量落 TimescaleDB）
/// </summary>
public interface IIotHistorySink
{
    /// <summary>
    /// 是否启用历史落库
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// 入队一条变量采集值（非阻塞，内部批量写入）
    /// </summary>
    void Enqueue(long deviceId, long variableId, object value, int status, DateTime timestamp);
}

/// <summary>
/// 时序历史查询器（由 Web.Core 实现，直接查 TimescaleDB）
/// </summary>
public interface IIotHistoryQuery
{
    Task<List<IotHistoryPointDto>> QueryAsync(IotHistoryQueryInput input);
}

/// <summary>
/// 历史数据配置选项（对应 appsettings 的 IotHistory 节）
/// </summary>
public class IotHistoryOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 时序库连接串（PostgreSQL + TimescaleDB 扩展）
    /// </summary>
    public string ConnectionString { get; set; }

    /// <summary>
    /// 批量写入阈值（达到即刷写）
    /// </summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// 刷写间隔（毫秒，到时即使不足批量也刷写）
    /// </summary>
    public int FlushIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 数据保留天数（TimescaleDB retention policy）
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}

/// <summary>
/// 历史查询入参
/// </summary>
public class IotHistoryQueryInput
{
    public long DeviceId { get; set; }

    public List<long> VariableIds { get; set; } = new();

    /// <summary>
    /// 起始时间，ISO 8601（推荐带时区，如 2026-11-22T14:34:32.000Z）；无时区时按 UTC 处理
    /// </summary>
    public string? StartTime { get; set; }

    /// <summary>
    /// 结束时间，ISO 8601（推荐带时区）；无时区时按 UTC 处理
    /// </summary>
    public string? EndTime { get; set; }

    /// <summary>
    /// 聚合方式：raw / avg / max / min / sum / count
    /// </summary>
    public string Aggregate { get; set; } = "raw";

    /// <summary>
    /// 降采样时间桶（秒），聚合时生效
    /// </summary>
    public int? BucketSeconds { get; set; }

    /// <summary>
    /// 返回点数上限
    /// </summary>
    public int? Limit { get; set; }
}

/// <summary>
/// 历史数据点
/// </summary>
public class IotHistoryPointDto
{
    /// <summary>
    /// 采样时间，ISO 8601 UTC（如 2026-11-22T14:34:32.227Z）
    /// </summary>
    public string Time { get; set; }

    public long VariableId { get; set; }

    public double? Value { get; set; }

    public int Status { get; set; }
}
