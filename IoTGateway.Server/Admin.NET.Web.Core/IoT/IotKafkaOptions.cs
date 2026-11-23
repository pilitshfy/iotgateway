namespace Admin.NET.Web.Core;

/// <summary>
/// Kafka 双通道配置（appsettings.json 的 "Kafka" 节点）
/// </summary>
public class IotKafkaOptions
{
    /// <summary>是否启用 Kafka 入站通道</summary>
    public bool Enabled { get; set; }

    /// <summary>Kafka 集群地址，如 127.0.0.1:9092</summary>
    public string BootstrapServers { get; set; } = "127.0.0.1:9092";

    /// <summary>入站消费组</summary>
    public string ConsumerGroup { get; set; } = "iotgateway-ingest";

    /// <summary>入站主题（边缘节点把心跳/遥测投递到此主题）</summary>
    public string IngestTopic { get; set; } = "iot.edge.ingest";

    /// <summary>是否把经 MQTT 接收的遥测转发到 Kafka（供上层系统消费）</summary>
    public bool ForwardEnabled { get; set; }

    /// <summary>转发主题</summary>
    public string ForwardTopic { get; set; } = "iot.telemetry";
}
