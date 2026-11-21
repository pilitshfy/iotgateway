using System.ComponentModel;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 系统传输配置服务
/// </summary>
[ApiDescriptionSettings(Order = 454)]
public class IotSystemConfigService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotSystemConfig> _configRep;
    private readonly IServiceProvider _serviceProvider;

    public IotSystemConfigService(SqlSugarRepository<IotSystemConfig> configRep, IServiceProvider serviceProvider)
    {
        _configRep = configRep;
        _serviceProvider = serviceProvider;
    }

    private IIotPlatformRuntime PlatformRuntime => _serviceProvider.GetService<IIotPlatformRuntime>();

    /// <summary>
    /// 获取传输配置
    /// </summary>
    [DisplayName("获取传输配置"), ApiDescriptionSettings(Name = "Get"), HttpGet]
    public async Task<IotSystemConfig> Get()
    {
        var config = await _configRep.AsQueryable().FirstAsync();
        return config ?? new IotSystemConfig
        {
            GatewayName = "IoTGateway",
            ClientId = "iotgateway",
            MqttIp = "127.0.0.1",
            MqttPort = 1888,
            IoTPlatformType = IotPlatformTypeEnum.IoTGateway
        };
    }

    /// <summary>
    /// 保存传输配置
    /// </summary>
    [DisplayName("保存传输配置"), ApiDescriptionSettings(Name = "Save"), HttpPost]
    public async Task Save([FromBody] IotSystemConfig input)
    {
        var db = await _configRep.AsQueryable().FirstAsync();
        if (db == null)
        {
            input.Id = 0;
            await _configRep.InsertAsync(input);
        }
        else
        {
            db.GatewayName = input.GatewayName;
            db.ClientId = input.ClientId;
            db.MqttIp = input.MqttIp;
            db.MqttPort = input.MqttPort;
            db.MqttUName = input.MqttUName;
            db.MqttUPwd = input.MqttUPwd;
            db.IoTPlatformType = input.IoTPlatformType;
            await _configRep.AsUpdateable(db).ExecuteCommandAsync();
        }

        if (PlatformRuntime != null) await PlatformRuntime.ReloadAsync();
    }
}
