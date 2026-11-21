using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Admin.NET.Application;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using Newtonsoft.Json;
using PluginInterface;
using PluginInterface.IoTSharp;
using PluginInterface.ThingsBoard;

namespace Admin.NET.Web.Core;

/// <summary>
/// 上游平台处理器（移植自原 Plugin/PlatformHandler）
/// </summary>
public interface IIotPlatformHandler
{
    IotPlatformTypeEnum PlatformType { get; }

    Task ClientConnectedAsync(IMqttClient client);

    Task ReceiveRpcAsync(string topic, string payload, Func<IotRpcRequestDto, Task> onRpc);

    Task ResponseRpcAsync(IMqttClient client, IotRpcResultDto response);

    Task PublishTelemetryAsync(IMqttClient client, string deviceName, IotDevice device, Dictionary<string, object> values);

    Task DeviceConnectedAsync(IMqttClient client, string deviceName);

    Task DeviceDisconnectedAsync(IMqttClient client, string deviceName);
}

/// <summary>
/// 平台处理器工厂
/// </summary>
public static class IotPlatformHandlerFactory
{
    public static IIotPlatformHandler Create(IotPlatformTypeEnum platform)
    {
        return platform switch
        {
            IotPlatformTypeEnum.ThingsBoard => new ThingsBoardPlatformHandler(),
            IotPlatformTypeEnum.ThingsCloud => new ThingsCloudPlatformHandler(),
            IotPlatformTypeEnum.ThingsPanel => new ThingsPanelPlatformHandler(),
            _ => new IoTSharpPlatformHandler()
        };
    }
}

internal static class PlatformPublish
{
    public static Task SendAsync(IMqttClient client, string topic, object payload, MqttQualityOfServiceLevel qos = MqttQualityOfServiceLevel.AtMostOnce)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(JsonConvert.SerializeObject(payload))
            .WithQualityOfServiceLevel(qos)
            .Build();
        return client.PublishAsync(message);
    }

    public static Dictionary<string, object> BuildTelemetryValues(Dictionary<string, object> values)
    {
        return values ?? new Dictionary<string, object>();
    }
}

/// <summary>
/// ThingsBoard 网关协议
/// </summary>
public class ThingsBoardPlatformHandler : IIotPlatformHandler
{
    private const string RpcTopic = "v1/gateway/rpc";

    public IotPlatformTypeEnum PlatformType => IotPlatformTypeEnum.ThingsBoard;

    public async Task ClientConnectedAsync(IMqttClient client)
    {
        await client.SubscribeAsync(RpcTopic, MqttQualityOfServiceLevel.ExactlyOnce);
        await client.SubscribeAsync("v1/gateway/attributes/response", MqttQualityOfServiceLevel.ExactlyOnce);
        await client.SubscribeAsync("v1/gateway/attributes", MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task ReceiveRpcAsync(string topic, string payload, Func<IotRpcRequestDto, Task> onRpc)
    {
        var request = JsonConvert.DeserializeObject<TBRpcRequest>(payload);
        if (request?.RequestData == null || string.IsNullOrWhiteSpace(request.RequestData.Method))
            return Task.CompletedTask;

        return onRpc(new IotRpcRequestDto
        {
            DeviceName = request.DeviceName,
            Method = request.RequestData.Method,
            RequestId = request.RequestData.RequestId,
            Params = request.RequestData.Params
        });
    }

    public Task ResponseRpcAsync(IMqttClient client, IotRpcResultDto response)
    {
        var payload = new TBRpcResponse
        {
            DeviceName = response.DeviceName,
            RequestId = response.RequestId,
            ResponseData = new Dictionary<string, object>
            {
                { "success", response.IsSuccess },
                { "description", response.Description }
            }
        };
        return PlatformPublish.SendAsync(client, RpcTopic, payload);
    }

    public Task PublishTelemetryAsync(IMqttClient client, string deviceName, IotDevice device, Dictionary<string, object> values)
    {
        var telemetry = new Dictionary<string, List<PayLoad>>
        {
            {
                deviceName, new List<PayLoad>
                {
                    new()
                    {
                        TS = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds,
                        DeviceStatus = DeviceStatusTypeEnum.Good,
                        Values = values
                    }
                }
            }
        };
        return PlatformPublish.SendAsync(client, "v1/gateway/telemetry", telemetry);
    }

    public Task DeviceConnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "v1/gateway/connect", new Dictionary<string, string> { { "device", deviceName } });
    }

    public Task DeviceDisconnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "v1/gateway/disconnect", new Dictionary<string, string> { { "device", deviceName } });
    }
}

/// <summary>
/// ThingsCloud 网关协议
/// </summary>
public class ThingsCloudPlatformHandler : IIotPlatformHandler
{
    public IotPlatformTypeEnum PlatformType => IotPlatformTypeEnum.ThingsCloud;

    public async Task ClientConnectedAsync(IMqttClient client)
    {
        foreach (var topic in new[]
                 {
                     "gateway/attributes/response",
                     "gateway/attributes/get/response",
                     "gateway/attributes/push",
                     "gateway/event/response",
                     "gateway/command/send"
                 })
        {
            await client.SubscribeAsync(topic, MqttQualityOfServiceLevel.ExactlyOnce);
        }
    }

    public Task ReceiveRpcAsync(string topic, string payload, Func<IotRpcRequestDto, Task> onRpc)
    {
        var request = JsonConvert.DeserializeObject<TCRpcRequest>(payload);
        if (request?.RequestData == null || string.IsNullOrWhiteSpace(request.RequestData.Method))
            return Task.CompletedTask;

        return onRpc(new IotRpcRequestDto
        {
            DeviceName = request.DeviceName,
            Method = request.RequestData.Method,
            RequestId = request.RequestData.RequestId,
            Params = request.RequestData.Params
        });
    }

    public Task ResponseRpcAsync(IMqttClient client, IotRpcResultDto response)
    {
        return Task.CompletedTask;
    }

    public Task PublishTelemetryAsync(IMqttClient client, string deviceName, IotDevice device, Dictionary<string, object> values)
    {
        var payload = new Dictionary<string, object> { { deviceName, values } };
        return PlatformPublish.SendAsync(client, "gateway/attributes", payload, MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task DeviceConnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "gateway/connect", new Dictionary<string, string> { { "device", deviceName } }, MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task DeviceDisconnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "gateway/disconnect", new Dictionary<string, string> { { "device", deviceName } }, MqttQualityOfServiceLevel.ExactlyOnce);
    }
}

/// <summary>
/// IoTSharp 网关协议
/// </summary>
public class IoTSharpPlatformHandler : IIotPlatformHandler
{
    public IotPlatformTypeEnum PlatformType => IotPlatformTypeEnum.IoTSharp;

    public async Task ClientConnectedAsync(IMqttClient client)
    {
        await client.SubscribeAsync("devices/+/rpc/request/+/+", MqttQualityOfServiceLevel.ExactlyOnce);
        await client.SubscribeAsync("devices/+/attributes/update", MqttQualityOfServiceLevel.ExactlyOnce);
        await client.SubscribeAsync("devices/+/attributes/response/+", MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task ReceiveRpcAsync(string topic, string payload, Func<IotRpcRequestDto, Task> onRpc)
    {
        var segments = topic.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 6) return Task.CompletedTask;

        var deviceName = segments[1];
        var method = segments[4];
        var requestId = segments[5];
        if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(requestId))
            return Task.CompletedTask;

        return onRpc(new IotRpcRequestDto
        {
            DeviceName = deviceName,
            Method = method,
            RequestId = requestId,
            Params = JsonConvert.DeserializeObject<Dictionary<string, object>>(payload)
        });
    }

    public Task ResponseRpcAsync(IMqttClient client, IotRpcResultDto response)
    {
        var payload = new ISRpcResponse
        {
            DeviceId = response.DeviceName,
            Method = response.Method,
            ResponseId = response.RequestId,
            Data = JsonConvert.SerializeObject(new Dictionary<string, object>
            {
                { "success", response.IsSuccess },
                { "description", response.Description }
            })
        };

        var topic = $"devices/{response.DeviceName}/rpc/response/{response.Method}/{response.RequestId}";
        return PlatformPublish.SendAsync(client, topic, payload, MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task PublishTelemetryAsync(IMqttClient client, string deviceName, IotDevice device, Dictionary<string, object> values)
    {
        return PlatformPublish.SendAsync(client, $"devices/{deviceName}/telemetry", values);
    }

    public Task DeviceConnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "v1/gateway/connect", new Dictionary<string, string> { { "device", deviceName } });
    }

    public Task DeviceDisconnectedAsync(IMqttClient client, string deviceName)
    {
        return PlatformPublish.SendAsync(client, "v1/gateway/disconnect", new Dictionary<string, string> { { "device", deviceName } });
    }
}

/// <summary>
/// ThingsPanel 网关协议
/// </summary>
public class ThingsPanelPlatformHandler : IIotPlatformHandler
{
    public IotPlatformTypeEnum PlatformType => IotPlatformTypeEnum.ThingsPanel;

    public async Task ClientConnectedAsync(IMqttClient client)
    {
        await client.SubscribeAsync("gateway/telemetry/control/+", MqttQualityOfServiceLevel.ExactlyOnce);
    }

    public Task ReceiveRpcAsync(string topic, string payload, Func<IotRpcRequestDto, Task> onRpc)
    {
        return Task.CompletedTask;
    }

    public Task ResponseRpcAsync(IMqttClient client, IotRpcResultDto response)
    {
        return Task.CompletedTask;
    }

    public Task PublishTelemetryAsync(IMqttClient client, string deviceName, IotDevice device, Dictionary<string, object> values)
    {
        var payload = new Dictionary<string, Dictionary<string, object>>
        {
            {
                "sub_device_data", new Dictionary<string, object> { { deviceName, values } }
            }
        };
        return PlatformPublish.SendAsync(client, "gateway/telemetry", payload);
    }

    public Task DeviceConnectedAsync(IMqttClient client, string deviceName)
    {
        return Task.CompletedTask;
    }

    public Task DeviceDisconnectedAsync(IMqttClient client, string deviceName)
    {
        return Task.CompletedTask;
    }
}
