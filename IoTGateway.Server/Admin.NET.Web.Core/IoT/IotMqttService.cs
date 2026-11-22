using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Admin.NET.Application;
using Admin.NET.Core;
using Furion.DependencyInjection;
using Furion.DynamicApiController;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// IoT MQTT 运行状态服务
/// </summary>
[ApiDescriptionSettings(Order = 456)]
public class IotMqttService : IDynamicApiController, ITransient
{
    private readonly IotMqttServer _mqttServer;
    private readonly SqlSugarRepository<IotSystemConfig> _configRep;
    private readonly IotOrgScopeService _orgScope;

    public IotMqttService(IotMqttServer mqttServer, SqlSugarRepository<IotSystemConfig> configRep, IotOrgScopeService orgScope)
    {
        _mqttServer = mqttServer;
        _configRep = configRep;
        _orgScope = orgScope;
    }

    /// <summary>
    /// MQTT 运行状态
    /// </summary>
    [DisplayName("MQTT运行状态"), HttpGet]
    public async Task<object> Status()
    {
        _orgScope.EnsureSuperAdmin();
        var config = await _configRep.AsQueryable().FirstAsync();

        return new
        {
            Running = _mqttServer.IsRunning,
            Port = _mqttServer.Port,
            Server = config?.MqttIp,
            GatewayName = config?.GatewayName,
            Clients = _mqttServer.ClientTopics.Select(u => new
            {
                ClientId = u.Key,
                Topics = u.Value.Keys.ToList()
            }).ToList()
        };
    }

    /// <summary>
    /// 发布 MQTT 消息（用于联调）
    /// </summary>
    [DisplayName("发布MQTT消息"), HttpPost]
    public async Task Publish([FromBody] IotMqttPublishInput input)
    {
        _orgScope.EnsureSuperAdmin();
        await _mqttServer.PublishAsync(input.Topic, input.Payload, input.Retain);
    }
}

/// <summary>
/// MQTT 发布输入
/// </summary>
public class IotMqttPublishInput
{
    public string Topic { get; set; }

    public string Payload { get; set; }

    public bool Retain { get; set; }
}
