using System.Threading.Tasks;

namespace Admin.NET.Application;

/// <summary>
/// 边缘节点下行通道（MQTT 下发，由 Web.Core 实现）
/// </summary>
public interface IIotEdgeDownlink
{
    /// <summary>
    /// MQTT Broker 是否运行
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// 下发消息到指定主题
    /// </summary>
    Task PublishAsync(string topic, string payload, bool retain = false);
}
