using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// 驱动管理
/// </summary>
[SugarTable("IotDriver", "驱动管理")]
public class IotDriver : EntityBase
{
    [SugarColumn(ColumnDescription = "驱动名", Length = 128)]
    public string DriverName { get; set; }

    [SugarColumn(ColumnDescription = "文件名", Length = 256)]
    public string FileName { get; set; }

    [SugarColumn(ColumnDescription = "程序集名", Length = 256)]
    public string AssembleName { get; set; }

    [SugarColumn(ColumnDescription = "剩余授权数")]
    public int AuthorizesNum { get; set; }
}

/// <summary>
/// 设备（分组/设备树）
/// </summary>
[SugarTable("IotDevice", "设备")]
public class IotDevice : EntityBase
{
    [SugarColumn(ColumnDescription = "名称", Length = 128)]
    public string DeviceName { get; set; }

    // SqlSugar CodeFirst 会按 Int32 解析文本默认值，实际根组织默认值由 IotOrgSchemaMigration 修正
    [SugarColumn(ColumnDescription = "所属组织Id", DefaultValue = "0")]
    public long OrgId { get; set; }

    /// <summary>
    /// 边缘节点Id（归属的边缘节点，可为空表示本机直采）
    /// </summary>
    [SugarColumn(ColumnDescription = "边缘节点Id", IsNullable = true)]
    public long? EdgeNodeId { get; set; }

    [SugarColumn(ColumnName = "SortIndex", ColumnDescription = "排序")]
    public int Index { get; set; }

    [SugarColumn(ColumnDescription = "描述", Length = 512, IsNullable = true)]
    public string Description { get; set; }

    [SugarColumn(ColumnDescription = "父级Id", IsNullable = true)]
    public long? ParentId { get; set; }

    [SugarColumn(ColumnDescription = "驱动Id", IsNullable = true)]
    public long? DriverId { get; set; }

    [SugarColumn(ColumnDescription = "启动")]
    public bool AutoStart { get; set; }

    [SugarColumn(ColumnDescription = "变化上传")]
    public bool CgUpload { get; set; }

    [SugarColumn(ColumnDescription = "归档周期ms")]
    public int EnforcePeriod { get; set; }

    [SugarColumn(ColumnDescription = "指令间隔ms")]
    public int CmdPeriod { get; set; }

    [SugarColumn(ColumnDescription = "类型")]
    public IotDeviceTypeEnum DeviceTypeEnum { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string DriverName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string OrgName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public List<IotDeviceConfig> DeviceConfigs { get; set; }

    [SugarColumn(IsIgnore = true)]
    public List<IotDeviceVariable> DeviceVariables { get; set; }
}

/// <summary>
/// 设备通讯配置
/// </summary>
[SugarTable("IotDeviceConfig", "通讯配置")]
public class IotDeviceConfig : EntityBase
{
    [SugarColumn(ColumnDescription = "名称", Length = 128)]
    public string DeviceConfigName { get; set; }

    [SugarColumn(ColumnDescription = "属性侧")]
    public IotDataSideEnum DataSide { get; set; }

    [SugarColumn(ColumnDescription = "描述", Length = 512, IsNullable = true)]
    public string Description { get; set; }

    [SugarColumn(ColumnName = "ConfigValue", ColumnDescription = "值", Length = 1024, IsNullable = true)]
    public string Value { get; set; }

    [SugarColumn(ColumnDescription = "备注", Length = 512, IsNullable = true)]
    public string EnumInfo { get; set; }

    [SugarColumn(ColumnDescription = "所属设备")]
    public long DeviceId { get; set; }
}

/// <summary>
/// 设备变量
/// </summary>
[SugarTable("IotDeviceVariable", "变量配置")]
public class IotDeviceVariable : EntityBase
{
    [SugarColumn(ColumnDescription = "变量名", Length = 128)]
    public string Name { get; set; }

    [SugarColumn(ColumnDescription = "描述", Length = 512, IsNullable = true)]
    public string Description { get; set; }

    [SugarColumn(ColumnDescription = "方法", Length = 128, IsNullable = true)]
    public string Method { get; set; }

    [SugarColumn(ColumnDescription = "地址", Length = 256, IsNullable = true)]
    public string DeviceAddress { get; set; }

    [SugarColumn(ColumnDescription = "数据类型")]
    public IotDataTypeEnum DataType { get; set; }

    [SugarColumn(ColumnDescription = "触发")]
    public bool IsTrigger { get; set; }

    [SugarColumn(ColumnDescription = "大小端")]
    public IotEndianEnum EndianType { get; set; }

    [SugarColumn(ColumnDescription = "表达式", Length = 512, IsNullable = true)]
    public string Expressions { get; set; }

    [SugarColumn(ColumnDescription = "上传")]
    public bool IsUpload { get; set; }

    [SugarColumn(ColumnDescription = "权限")]
    public IotProtectTypeEnum ProtectType { get; set; }

    [SugarColumn(ColumnName = "SortIndex", ColumnDescription = "排序")]
    public int Index { get; set; }

    [SugarColumn(ColumnDescription = "所属设备")]
    public long DeviceId { get; set; }

    [SugarColumn(ColumnDescription = "别名", Length = 128, IsNullable = true)]
    public string Alias { get; set; }

    [SugarColumn(IsIgnore = true)]
    public object Value { get; set; }

    [SugarColumn(IsIgnore = true)]
    public object CookedValue { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string Message { get; set; }

    [SugarColumn(IsIgnore = true)]
    public DateTime Timestamp { get; set; }

    [SugarColumn(IsIgnore = true)]
    public IotVariableStatusEnum StatusType { get; set; } = IotVariableStatusEnum.UnKnow;

    [SugarColumn(IsIgnore = true)]
    public object[] Values { get; set; } = new object[3];

    [SugarColumn(IsIgnore = true)]
    public object[] CookedValues { get; set; } = new object[3];

    public void EnqueueVariable(object value)
    {
        Values[2] = Values[1];
        Values[1] = Values[0];
        Values[0] = value;
    }

    public void EnqueueCookedVariable(object value)
    {
        CookedValues[2] = CookedValues[1];
        CookedValues[1] = CookedValues[0];
        CookedValues[0] = value;
    }
}

/// <summary>
/// 系统传输配置
/// </summary>
[SugarTable("IotSystemConfig", "传输配置")]
public class IotSystemConfig : EntityBase
{
    [SugarColumn(ColumnDescription = "网关名称", Length = 128)]
    public string GatewayName { get; set; }

    [SugarColumn(ColumnDescription = "ClientId", Length = 128)]
    public string ClientId { get; set; }

    [SugarColumn(ColumnDescription = "Mqtt服务器", Length = 128)]
    public string MqttIp { get; set; }

    [SugarColumn(ColumnDescription = "Mqtt端口")]
    public int MqttPort { get; set; }

    [SugarColumn(ColumnDescription = "Mqtt用户名", Length = 128, IsNullable = true)]
    public string MqttUName { get; set; }

    [SugarColumn(ColumnDescription = "Mqtt密码", Length = 128, IsNullable = true)]
    public string MqttUPwd { get; set; }

    [SugarColumn(ColumnDescription = "输出平台")]
    public IotPlatformTypeEnum IoTPlatformType { get; set; }
}

/// <summary>
/// RPC 日志
/// </summary>
[SugarTable("IotRpcLog", "RPC日志")]
public class IotRpcLog : EntityBase
{
    [SugarColumn(ColumnDescription = "发起方")]
    public IotRpcSideEnum RpcSide { get; set; }

    [SugarColumn(ColumnDescription = "开始时间")]
    public DateTime StartTime { get; set; }

    [SugarColumn(ColumnDescription = "设备Id", IsNullable = true)]
    public long? DeviceId { get; set; }

    [SugarColumn(ColumnDescription = "方法", Length = 128, IsNullable = true)]
    public string Method { get; set; }

    [SugarColumn(ColumnName = "RpcParams", ColumnDescription = "请求参数", Length = 2048, IsNullable = true)]
    public string Params { get; set; }

    [SugarColumn(ColumnDescription = "结束时间")]
    public DateTime EndTime { get; set; }

    [SugarColumn(ColumnDescription = "是否成功")]
    public bool IsSuccess { get; set; }

    [SugarColumn(ColumnDescription = "描述", Length = 512, IsNullable = true)]
    public string Description { get; set; }
}

/// <summary>
/// 边缘节点（P0 模型：集团级边缘接入节点注册/心跳/在线状态）
/// </summary>
[SugarTable("IotEdgeNode", "边缘节点")]
public class IotEdgeNode : EntityBase
{
    /// <summary>节点名称</summary>
    [SugarColumn(ColumnDescription = "节点名称", Length = 128)]
    public string Name { get; set; }

    /// <summary>节点编号（唯一，用于设备接入鉴权/寻址）</summary>
    [SugarColumn(ColumnDescription = "节点编号", Length = 128)]
    public string NodeCode { get; set; }

    /// <summary>所属组织Id</summary>
    [SugarColumn(ColumnDescription = "所属组织Id", DefaultValue = "0")]
    public long OrgId { get; set; }

    /// <summary>是否启用</summary>
    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    /// <summary>是否在线（由心跳更新）</summary>
    [SugarColumn(ColumnDescription = "是否在线", IsNullable = true)]
    public bool? Online { get; set; }

    /// <summary>固件/客户端版本</summary>
    [SugarColumn(ColumnDescription = "版本", Length = 64, IsNullable = true)]
    public string Version { get; set; }

    /// <summary>最后心跳时间</summary>
    [SugarColumn(ColumnDescription = "最后心跳时间", IsNullable = true)]
    public DateTime? LastHeartbeatTime { get; set; }

    /// <summary>最后上报时间</summary>
    [SugarColumn(ColumnDescription = "最后上报时间", IsNullable = true)]
    public DateTime? LastReportTime { get; set; }

    /// <summary>备注</summary>
    [SugarColumn(ColumnDescription = "备注", Length = 512, IsNullable = true)]
    public string Remark { get; set; }

    /// <summary>组织名称（不映射数据库）</summary>
    [SugarColumn(IsIgnore = true)]
    public string OrgName { get; set; }
}
