using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Admin.NET.Web.Core;

/// <summary>
/// IoT MQTT Broker（监听 1888 端口，TCP 与 WebSocket 共用）
/// 由 MQTTnet.AspNetCore 托管，本类负责事件与消息注入。
/// </summary>
public class IotMqttServer
{
    private readonly ILogger<IotMqttServer> _logger;
    private MqttServer _server;

    public IotMqttServer(ILogger<IotMqttServer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 收到客户端发布的消息（供 RPC 等服务订阅）
    /// </summary>
    public event Func<string, string, Task> MessageReceived;

    /// <summary>
    /// 客户端订阅关系：ClientId -> Topics
    /// </summary>
    public ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> ClientTopics { get; } = new();

    public bool IsRunning => _server != null;

    public int Port { get; } = 1888;

    /// <summary>
    /// 绑定托管服务器并挂载事件
    /// </summary>
    public void Attach(MqttServer server)
    {
        if (server == null || ReferenceEquals(_server, server)) return;

        _server = server;

        _server.StartedAsync += _ =>
        {
            _logger.LogInformation("IoT MQTT Broker 已启动，TCP/WebSocket 端口 {Port}", Port);
            return Task.CompletedTask;
        };

        _server.StoppedAsync += _ =>
        {
            _logger.LogInformation("IoT MQTT Broker 已停止");
            return Task.CompletedTask;
        };

        _server.ClientConnectedAsync += e =>
        {
            _logger.LogInformation("MQTT 客户端 [{ClientId}] {Endpoint} 已连接", e.ClientId, e.Endpoint);
            if (IsInternalClient(e.ClientId))
                ClientTopics.TryAdd(e.ClientId, new ConcurrentDictionary<string, byte>());
            return Task.CompletedTask;
        };

        _server.ClientDisconnectedAsync += e =>
        {
            _logger.LogInformation("MQTT 客户端 [{ClientId}] 已断开：{Type}", e.ClientId, e.DisconnectType);
            if (IsInternalClient(e.ClientId))
                ClientTopics.TryRemove(e.ClientId, out _);
            return Task.CompletedTask;
        };

        _server.ClientSubscribedTopicAsync += e =>
        {
            if (!IsInternalClient(e.ClientId)) return Task.CompletedTask;

            var topics = ClientTopics.GetOrAdd(e.ClientId, _ => new ConcurrentDictionary<string, byte>());
            topics.TryAdd(e.TopicFilter.Topic, 0);
            return Task.CompletedTask;
        };

        _server.ClientUnsubscribedTopicAsync += e =>
        {
            if (!IsInternalClient(e.ClientId)) return Task.CompletedTask;

            if (!ClientTopics.TryGetValue(e.ClientId, out var topics)) return Task.CompletedTask;
            if (topics.TryRemove(e.TopicFilter, out _) && topics.IsEmpty)
                ClientTopics.TryRemove(e.ClientId, out _);
            return Task.CompletedTask;
        };

        _server.ValidatingConnectionAsync += e =>
        {
            // 内部客户端（WebSocket 调试）直接放行；外部客户端保持与原网关一致
            e.ReasonCode = MqttConnectReasonCode.Success;
            return Task.CompletedTask;
        };

        _server.InterceptingPublishAsync += e =>
        {
            var handler = MessageReceived;
            if (handler == null) return Task.CompletedTask;

            var topic = e.ApplicationMessage.Topic;
            var payload = e.ApplicationMessage.ConvertPayloadToString();
            if (string.IsNullOrWhiteSpace(topic)) return Task.CompletedTask;

            _ = Task.Run(async () =>
            {
                try
                {
                    await handler(topic, payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理 MQTT 消息失败：{Topic}", topic);
                }
            });

            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// 注入消息（用于网关内部实时刷新）
    /// </summary>
    public async Task InjectAsync(InjectedMqttApplicationMessage message)
    {
        if (_server == null) return;
        await _server.InjectApplicationMessage(message);
    }

    /// <summary>
    /// 发布消息
    /// </summary>
    public async Task PublishAsync(string topic, string payload, bool retain = false)
    {
        if (_server == null) return;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithRetainFlag(retain)
            .Build();

        await _server.InjectApplicationMessage(new InjectedMqttApplicationMessage(message)
        {
            SenderClientId = "iotgateway"
        });
    }

    public bool CanPub(string variableId)
    {
        return ClientTopics.Any(x => x.Value.Keys.Any(y => y.EndsWith(variableId)));
    }

    private static bool IsInternalClient(string clientId)
    {
        return clientId != null && clientId.StartsWith("iotgatewayInternal_");
    }
}
