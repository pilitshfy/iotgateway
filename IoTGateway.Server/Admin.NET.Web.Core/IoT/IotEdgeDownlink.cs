using System.Threading.Tasks;
using Admin.NET.Application;

namespace Admin.NET.Web.Core;

/// <summary>
/// 边缘节点下行通道实现（基于内嵌 MQTT Broker）
/// </summary>
public class IotEdgeDownlink : IIotEdgeDownlink
{
    private readonly IotMqttServer _mqtt;

    public IotEdgeDownlink(IotMqttServer mqtt)
    {
        _mqtt = mqtt;
    }

    public bool IsRunning => _mqtt.IsRunning;

    public Task PublishAsync(string topic, string payload, bool retain = false) =>
        _mqtt.PublishAsync(topic, payload, retain);
}
