using System;
using System.Text.Json;
using System.Threading.Tasks;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Admin.NET.Web.Core;

/// <summary>
/// Kafka 生产者：把 MQTT 通道接收的遥测/心跳转发到 Kafka 供上层系统消费（Kafka 双通道的出向）。
/// 仅当 Kafka:Enabled 且 Kafka:ForwardEnabled 为 true 时生效，否则全部为空操作。
/// </summary>
public class IotKafkaProducer : IDisposable
{
    private readonly IotKafkaOptions _options;
    private readonly ILogger<IotKafkaProducer> _logger;
    private IProducer<string, string> _producer;

    public IotKafkaProducer(IOptions<IotKafkaOptions> options, ILogger<IotKafkaProducer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private bool Active => _options.Enabled && _options.ForwardEnabled;

    /// <summary>异步转发一条入站消息（不阻塞主流程）</summary>
    public void Forward(string nodeCode, string kind, string payload)
    {
        if (!Active || string.IsNullOrWhiteSpace(nodeCode)) return;

        var value = JsonSerializer.Serialize(new
        {
            nodeCode,
            type = kind,
            payload,
            ts = DateTime.UtcNow.ToString("o")
        });

        _ = Task.Run(() =>
        {
            try
            {
                _producer ??= new ProducerBuilder<string, string>(new ProducerConfig
                {
                    BootstrapServers = _options.BootstrapServers,
                    Acks = Acks.Leader,
                    MessageTimeoutMs = 3000
                }).Build();

                _producer.Produce(_options.ForwardTopic, new Message<string, string> { Key = nodeCode, Value = value },
                    r => { if (r.Error.IsError) _logger.LogWarning("Kafka 转发失败：{Reason}", r.Error.Reason); });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kafka 转发异常（主题 {Topic}）", _options.ForwardTopic);
            }
        });
    }

    public void Dispose()
    {
        try { _producer?.Flush(TimeSpan.FromSeconds(2)); } catch { }
        _producer?.Dispose();
    }
}
