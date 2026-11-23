using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.NET.Web.Core;

/// <summary>
/// Kafka 入站通道：从 Kafka 主题消费边缘节点上报，复用 <see cref="IotEdgeIngestProcessor"/> 落库/告警。
/// 与 MQTT 通道并列，二者解出统一消息后交给同一处理器，切换通道不影响下游业务。
///
/// 消费的消息体约定（JSON）：
///   { "nodeCode": "PLANT-EDGE-01", "type": "telemetry|heartbeat", "payload": { ... } }
/// 其中 payload 可为对象或字符串（字符串需内含与 MQTT 通道相同的 JSON）。
/// </summary>
public class IotKafkaIngestService : BackgroundService
{
    private readonly IotKafkaOptions _options;
    private readonly IotEdgeIngestProcessor _processor;
    private readonly ILogger<IotKafkaIngestService> _logger;

    public IotKafkaIngestService(IOptions<IotKafkaOptions> options, IotEdgeIngestProcessor processor, ILogger<IotKafkaIngestService> logger)
    {
        _options = options.Value;
        _processor = processor;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Kafka 入站通道未启用（Kafka:Enabled=false）");
            return Task.CompletedTask;
        }
        // 消费循环放到后台线程，避免阻塞宿主启动
        return Task.Run(() => ConsumeLoopAsync(stoppingToken), stoppingToken);
    }

    private async Task ConsumeLoopAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = true
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => _logger.LogWarning("Kafka 客户端错误：{Reason}", e.Reason))
            .Build();
        consumer.Subscribe(_options.IngestTopic);
        _logger.LogInformation("Kafka 入站通道已启动：{Brokers}，主题 {Topic}，消费组 {Group}",
            _options.BootstrapServers, _options.IngestTopic, _options.ConsumerGroup);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogWarning(ex, "Kafka 消费异常，继续");
                    continue;
                }
                if (result?.Message?.Value == null) continue;

                try
                {
                    await DispatchAsync(result.Message.Value);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Kafka 消息处理失败：{Value}", result.Message.Value);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { consumer.Close(); } catch { }
            _logger.LogInformation("Kafka 入站通道已停止");
        }
    }

    private async Task DispatchAsync(string value)
    {
        using var doc = JsonDocument.Parse(value);
        var root = doc.RootElement;

        var nodeCode = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("nodeCode", out var n)
            && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(nodeCode))
        {
            _logger.LogWarning("Kafka 消息缺少 nodeCode，已跳过");
            return;
        }

        var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString() : "telemetry";

        string payload;
        if (root.TryGetProperty("payload", out var p))
            payload = p.ValueKind == JsonValueKind.String ? p.GetString() : p.GetRawText();
        else
            payload = value;

        await _processor.HandleAsync(nodeCode, type, payload);
    }
}
