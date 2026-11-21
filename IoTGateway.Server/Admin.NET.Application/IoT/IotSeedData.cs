namespace Admin.NET.Application;

/// <summary>
/// IoT 业务菜单种子数据
/// </summary>
public class IotMenuSeedData : ISqlSugarEntitySeedData<SysMenu>
{
    public IEnumerable<SysMenu> HasData()
    {
        return new[]
        {
            new SysMenu{ Id=1311000000101, Pid=0, Title="IoT网关", Path="/iot", Name="iot", Component="Layout", Icon="ele-Cpu", Type=MenuTypeEnum.Dir, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=500 },

            new SysMenu{ Id=1311000010101, Pid=1311000000101, Title="设备管理", Path="/iot/device", Name="iotDevice", Component="/iot/device/index", Icon="ele-Connection", Type=MenuTypeEnum.Menu, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=100 },
            new SysMenu{ Id=1311000010102, Pid=1311000010101, Title="查询", Permission="iotDevice:page", Type=MenuTypeEnum.Btn, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=100 },
            new SysMenu{ Id=1311000010103, Pid=1311000010101, Title="增加", Permission="iotDevice:add", Type=MenuTypeEnum.Btn, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=100 },
            new SysMenu{ Id=1311000010104, Pid=1311000010101, Title="编辑", Permission="iotDevice:update", Type=MenuTypeEnum.Btn, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=100 },
            new SysMenu{ Id=1311000010105, Pid=1311000010101, Title="删除", Permission="iotDevice:delete", Type=MenuTypeEnum.Btn, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=100 },

            new SysMenu{ Id=1311000010201, Pid=1311000000101, Title="驱动管理", Path="/iot/driver", Name="iotDriver", Component="/iot/driver/index", Icon="ele-Box", Type=MenuTypeEnum.Menu, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=110 },
            new SysMenu{ Id=1311000010301, Pid=1311000000101, Title="传输配置", Path="/iot/config", Name="iotConfig", Component="/iot/config/index", Icon="ele-Setting", Type=MenuTypeEnum.Menu, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=120 },
            new SysMenu{ Id=1311000010401, Pid=1311000000101, Title="MQTT状态", Path="/iot/mqtt", Name="iotMqtt", Component="/iot/mqtt/index", Icon="ele-Monitor", Type=MenuTypeEnum.Menu, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=130 },
            new SysMenu{ Id=1311000010501, Pid=1311000000101, Title="RPC日志", Path="/iot/rpc", Name="iotRpc", Component="/iot/rpc/index", Icon="ele-Link", Type=MenuTypeEnum.Menu, CreateTime=DateTime.Parse("2026-11-21 00:00:00"), OrderNo=140 }
        };
    }
}

/// <summary>
/// IoT 菜单租户绑定种子数据（超管登录菜单范围取自 SysTenantMenu）
/// </summary>
public class IotTenantMenuSeedData : ISqlSugarEntitySeedData<SysTenantMenu>
{
    public IEnumerable<SysTenantMenu> HasData()
    {
        return new IotMenuSeedData().HasData().Select(u => new SysTenantMenu
        {
            Id = CommonUtil.GetFixedHashCode("" + SqlSugarConst.DefaultTenantId + u.Id, 1300000000000),
            TenantId = SqlSugarConst.DefaultTenantId,
            MenuId = u.Id
        });
    }
}

/// <summary>
/// IoT 传输配置种子数据
/// </summary>
[IgnoreUpdateSeed]
public class IotSystemConfigSeedData : ISqlSugarEntitySeedData<IotSystemConfig>
{
    public IEnumerable<IotSystemConfig> HasData()
    {
        return new[]
        {
            new IotSystemConfig
            {
                Id = 1311000000201,
                GatewayName = "IoTGateway",
                ClientId = "iotgateway",
                MqttIp = "127.0.0.1",
                MqttPort = 1888,
                IoTPlatformType = IotPlatformTypeEnum.IoTGateway,
                CreateTime = DateTime.Parse("2026-11-21 00:00:00")
            }
        };
    }
}
