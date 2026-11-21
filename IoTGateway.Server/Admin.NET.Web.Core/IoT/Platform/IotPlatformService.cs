using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 上游平台连接与输出（移植自原 Plugin/MessageService）
/// </summary>
public class IotPlatformService : BackgroundService, IIotPlatformRuntime
{
    private readonly ISqlSugarClient _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IotPlatformService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IMqttClient _client;
    private IIotPlatformHandler _handler;
    private IotSystemConfig _config;

    public IotPlatformService(ISqlSugarClient db, IServiceProvider serviceProvider, ILogger<IotPlatformService> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(5000, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await ReloadAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_client != null && !_client.IsConnected)
                    await TryConnectAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("上游平台 MQTT 重连失败：{Message}", ex.Message);
            }

            try
            {
                await Task.Delay(5000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// 重新加载平台配置与连接
    /// </summary>
    public async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await DisconnectAsync();

            _config = await _db.Queryable<IotSystemConfig>().FirstAsync();
            if (_config == null || _config.IoTPlatformType == IotPlatformTypeEnum.IoTGateway)
            {
                _logger.LogInformation("上游平台类型为 IoTGateway，无需建立平台连接");
                return;
            }

            if (string.IsNullOrWhiteSpace(_config.MqttIp) || _config.MqttPort <= 0)
            {
                _logger.LogWarning("上游平台 MQTT 地址未配置，跳过平台连接");
                return;
            }

            _handler = IotPlatformHandlerFactory.Create(_config.IoTPlatformType);
            _client = new MqttFactory().CreateMqttClient();
            _client.ConnectedAsync += Client_ConnectedAsync;
            _client.DisconnectedAsync += Client_DisconnectedAsync;
            _client.ApplicationMessageReceivedAsync += Client_ApplicationMessageReceivedAsync;

            await TryConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载上游平台配置失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task TryConnectAsync()
    {
        if (_client == null || _config == null) return;

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(_config.MqttIp, _config.MqttPort)
            .WithClientId(string.IsNullOrWhiteSpace(_config.ClientId) ? Guid.NewGuid().ToString() : _config.ClientId)
            .WithTimeout(TimeSpan.FromSeconds(30))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(60))
            .WithProtocolVersion(MqttProtocolVersion.V311)
            .WithCleanSession(true);

        if (!string.IsNullOrWhiteSpace(_config.MqttUName))
            builder.WithCredentials(_config.MqttUName, _config.MqttUPwd);

        await _client.ConnectAsync(builder.Build());
        _logger.LogInformation("上游平台 MQTT 已连接：{Ip}:{Port}，平台 {Platform}", _config.MqttIp, _config.MqttPort, _config.IoTPlatformType);
    }

    private async Task Client_ConnectedAsync(MqttClientConnectedEventArgs arg)
    {
        if (_handler == null) return;
        try
        {
            await _handler.ClientConnectedAsync(_client);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "平台处理器订阅失败");
        }
    }

    private Task Client_DisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        _logger.LogWarning("上游平台 MQTT 已断开：{Reason}", arg.Reason);
        return Task.CompletedTask;
    }

    private async Task Client_ApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        if (_handler == null) return;

        try
        {
            await _handler.ReceiveRpcAsync(e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString(), ExecuteRpcAsync);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理平台消息失败：{Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task ExecuteRpcAsync(IotRpcRequestDto request)
    {
        var runtime = _serviceProvider.GetService<IIotDeviceRuntime>();
        if (runtime == null) return;

        var result = await runtime.ExecuteRpcAsync(request);
        if (_handler != null && _client != null && _client.IsConnected)
            await _handler.ResponseRpcAsync(_client, result);
    }

    private async Task DisconnectAsync()
    {
        if (_client == null) return;

        try
        {
            _client.ConnectedAsync -= Client_ConnectedAsync;
            _client.DisconnectedAsync -= Client_DisconnectedAsync;
            _client.ApplicationMessageReceivedAsync -= Client_ApplicationMessageReceivedAsync;
            if (_client.IsConnected) await _client.DisconnectAsync();
            _client.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("断开上游平台连接失败：{Message}", ex.Message);
        }
        finally
        {
            _client = null;
            _handler = null;
        }
    }

    private bool CanPublish => _handler != null && _client != null && _client.IsConnected;

    public async Task PublishTelemetryAsync(string deviceName, IotDevice device, Dictionary<string, object> values)
    {
        if (!CanPublish) return;
        try
        {
            await _handler.PublishTelemetryAsync(_client, deviceName, device, values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传平台遥测失败：{DeviceName}", deviceName);
        }
    }

    public async Task DeviceConnectedAsync(string deviceName)
    {
        if (!CanPublish) return;
        try
        {
            await _handler.DeviceConnectedAsync(_client, deviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传平台设备上线失败：{DeviceName}", deviceName);
        }
    }

    public async Task DeviceDisconnectedAsync(string deviceName)
    {
        if (!CanPublish) return;
        try
        {
            await _handler.DeviceDisconnectedAsync(_client, deviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "上传平台设备离线失败：{DeviceName}", deviceName);
        }
    }
}
