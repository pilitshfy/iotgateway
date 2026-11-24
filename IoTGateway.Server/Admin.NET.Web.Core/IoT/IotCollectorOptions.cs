namespace Admin.NET.Web.Core;

/// <summary>
/// 采集运行时多实例分片配置：同一套程序与配置库，按实例分配分片
/// </summary>
public class IotCollectorOptions
{
    /// <summary>
    /// 是否启用采集。Web/API 实例可设为 false，本实例不加载任何设备
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 当前实例分片序号（从 0 开始）
    /// </summary>
    public int ShardIndex { get; set; }

    /// <summary>
    /// 分片总数；1 表示不拆分（单实例采集全部设备）
    /// </summary>
    public int ShardCount { get; set; } = 1;

    /// <summary>
    /// 配置变更同步间隔（秒）；0 表示不轮询，配置变更需重启实例生效
    /// </summary>
    public int SyncSeconds { get; set; } = 30;

    /// <summary>
    /// 规范化分片参数，返回 false 表示配置无效（已回退为单分片）
    /// </summary>
    public bool Normalize()
    {
        if (ShardCount < 1) ShardCount = 1;
        if (SyncSeconds < 0) SyncSeconds = 0;

        if (ShardIndex < 0 || ShardIndex >= ShardCount)
        {
            ShardIndex = 0;
            ShardCount = 1;
            return false;
        }

        return true;
    }
}
