using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Admin.NET.Web.Core;

/// <summary>
/// 边缘节点入站服务（MQTT 通道）：订阅内嵌 MQTT broker，把消息交给统一处理器，
/// 并（可选）转发到 Kafka 供上层系统消费。Kafka 入站见 <see cref="IotKafkaIngestService"/>。
/// 主题约定：
///   edge/{nodeCode}/heartbeat  -> 在线心跳（payload 可选 {"version":"x.y.z"}）
///   edge/{nodeCode}/telemetry  -> 遥测上报（payload 单条对象或对象数组）
/// </summary>
public class IotEdgeIngestService : BackgroundService
{
    private readonly IotMqttServer _mqtt;
    private readonly IotEdgeIngestProcessor _processor;
    private readonly IotKafkaProducer _forwarder;
    private readonly ILogger<IotEdgeIngestService> _logger;
    private readonly TaskCompletionSource _stopped = new();

    public IotEdgeIngestService(
        IotMqttServer mqtt,
        IotEdgeIngestProcessor processor,
        IotKafkaProducer forwarder,
        ILogger<IotEdgeIngestService> logger)
    {
        _mqtt = mqtt;
        _processor = processor;
        _forwarder = forwarder;
        _logger = logger;
        _mqtt.MessageReceived += OnMessageAsync;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _stopped.Task.WaitAsync(stoppingToken);

    public override void Dispose()
    {
        _mqtt.MessageReceived -= OnMessageAsync;
        _stopped.TrySetResult();
        base.Dispose();
    }

    private async Task OnMessageAsync(string topic, string payload)
    {
        try
        {
            if (string.IsNullOrEmpty(topic) || !topic.StartsWith("edge/", StringComparison.OrdinalIgnoreCase)) return;
            var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return; // edge/{nodeCode}/{kind}
            var nodeCode = parts[1];
            var kind = parts[2];

            await _processor.HandleAsync(nodeCode, kind, payload);
            _forwarder.Forward(nodeCode, kind, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理边缘节点入站消息失败：{Topic}", topic);
        }
    }
}
