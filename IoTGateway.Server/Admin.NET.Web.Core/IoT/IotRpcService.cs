using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using Furion.DependencyInjection;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Admin.NET.Web.Core;

/// <summary>
/// 内部 MQTT RPC 运行时（移植自原 DeviceThread.MyMqttClient_OnExcRpc）
/// 请求主题：internal/v1/gateway/rpc/{deviceName}/{requestId}
/// 应答主题：internal/v1/gateway/rpc/response/{deviceName}/{requestId}
/// </summary>
public class IotRpcRuntime : BackgroundService
{
    private const string RequestPrefix = "internal/v1/gateway/rpc/";

    private readonly IotMqttServer _mqttServer;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IotRpcRuntime> _logger;

    public IotRpcRuntime(IotMqttServer mqttServer, IServiceProvider serviceProvider, ILogger<IotRpcRuntime> logger)
    {
        _mqttServer = mqttServer;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _mqttServer.MessageReceived += HandleMessageAsync;
    }

    private async Task HandleMessageAsync(string topic, string payload)
    {
        if (!topic.StartsWith(RequestPrefix, StringComparison.Ordinal)) return;
        if (topic.StartsWith(RequestPrefix + "response/", StringComparison.Ordinal)) return;

        var segments = topic.Substring(RequestPrefix.Length).Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) return;

        var deviceName = segments[0];
        var requestId = segments[1];

        IotRpcRequestDto request;
        try
        {
            request = JsonConvert.DeserializeObject<IotRpcRequestDto>(payload) ?? new IotRpcRequestDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析 RPC 请求失败：{Topic}", topic);
            return;
        }

        request.DeviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? deviceName : request.DeviceName;
        request.RequestId = string.IsNullOrWhiteSpace(request.RequestId) ? requestId : request.RequestId;
        if (string.IsNullOrWhiteSpace(request.Method)) return;

        var runtime = _serviceProvider.GetService<IIotDeviceRuntime>();
        if (runtime == null) return;

        var result = await runtime.ExecuteRpcAsync(request);
        var responseTopic = $"internal/v1/gateway/rpc/response/{deviceName}/{requestId}";
        await _mqttServer.PublishAsync(responseTopic, JsonConvert.SerializeObject(result));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }
}

/// <summary>
/// RPC 调试接口（前端或联调可直接触发写入）
/// </summary>
[ApiDescriptionSettings(Order = 457)]
public class IotRpcInvokeService : IDynamicApiController, ITransient
{
    private readonly IServiceProvider _serviceProvider;

    public IotRpcInvokeService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 执行 RPC
    /// </summary>
    [DisplayName("执行RPC"), ApiDescriptionSettings(Name = "Invoke"), HttpPost]
    public async Task<IotRpcResultDto> Invoke([FromBody] IotRpcRequestDto input)
    {
        input ??= new IotRpcRequestDto();
        input.RequestId ??= Guid.NewGuid().ToString();

        var runtime = _serviceProvider.GetService<IIotDeviceRuntime>();
        if (runtime == null)
        {
            return new IotRpcResultDto
            {
                DeviceName = input.DeviceName,
                Method = input.Method,
                RequestId = input.RequestId,
                IsSuccess = false,
                Description = "设备运行时未启动"
            };
        }

        return await runtime.ExecuteRpcAsync(input);
    }
}
