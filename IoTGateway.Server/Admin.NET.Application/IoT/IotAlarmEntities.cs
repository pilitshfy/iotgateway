using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// 告警规则（同一设备内的一个或多个变量组合判断）
/// </summary>
[SugarTable("IotAlarmRule", "告警规则")]
public class IotAlarmRule : EntityBase
{
    [SugarColumn(ColumnDescription = "规则名称", Length = 128)]
    public string Name { get; set; }

    [SugarColumn(ColumnDescription = "所属组织Id")]
    public long OrgId { get; set; }

    [SugarColumn(ColumnDescription = "设备Id")]
    public long DeviceId { get; set; }

    [SugarColumn(ColumnDescription = "告警级别")]
    public IotAlarmSeverityEnum Severity { get; set; } = IotAlarmSeverityEnum.Warning;

    [SugarColumn(ColumnDescription = "条件组合方式")]
    public IotAlarmCombineModeEnum CombineMode { get; set; } = IotAlarmCombineModeEnum.All;

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(ColumnDescription = "连续触发样本数")]
    public int TriggerSamples { get; set; } = 1;

    [SugarColumn(ColumnDescription = "连续恢复样本数")]
    public int RecoverySamples { get; set; } = 1;

    [SugarColumn(ColumnDescription = "活动告警重复通知间隔秒数，0表示不重复")]
    public int SuppressSeconds { get; set; }

    [SugarColumn(ColumnDescription = "通知渠道Id列表", IsJson = true, IsNullable = true, ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public List<long> ChannelIds { get; set; } = new();

    [SugarColumn(ColumnDescription = "备注", Length = 512, IsNullable = true)]
    public string Remark { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string DeviceName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string OrgName { get; set; }

    [SugarColumn(IsIgnore = true)]
    public List<IotAlarmCondition> Conditions { get; set; } = new();
}

/// <summary>
/// 告警条件
/// </summary>
[SugarTable("IotAlarmCondition", "告警条件")]
public class IotAlarmCondition : EntityBase
{
    [SugarColumn(ColumnDescription = "规则Id")]
    public long RuleId { get; set; }

    [SugarColumn(ColumnDescription = "变量Id")]
    public long VariableId { get; set; }

    [SugarColumn(ColumnDescription = "比较方式")]
    public IotAlarmOperatorEnum Operator { get; set; }

    [SugarColumn(ColumnDescription = "阈值", IsNullable = true)]
    public double? Value { get; set; }

    [SugarColumn(ColumnDescription = "区间上限", IsNullable = true)]
    public double? SecondValue { get; set; }

    [SugarColumn(ColumnDescription = "变量状态", IsNullable = true)]
    public IotVariableStatusEnum? Status { get; set; }

    [SugarColumn(ColumnDescription = "排序")]
    public int Sort { get; set; }

    [SugarColumn(IsIgnore = true)]
    public string VariableName { get; set; }
}

/// <summary>
/// 告警事件，组织、设备与规则名称均为触发时快照
/// </summary>
[SugarTable("IotAlarmEvent", "告警事件")]
[SugarIndex("index_{table}_RuleStatus", nameof(RuleId), OrderByType.Asc, nameof(Status), OrderByType.Asc)]
[SugarIndex("index_{table}_OrgTrigger", nameof(OrgId), OrderByType.Asc, nameof(TriggerTime), OrderByType.Desc)]
public class IotAlarmEvent : EntityBase
{
    [SugarColumn(ColumnDescription = "规则Id")]
    public long RuleId { get; set; }

    [SugarColumn(ColumnDescription = "规则名称", Length = 128)]
    public string RuleName { get; set; }

    [SugarColumn(ColumnDescription = "组织Id快照")]
    public long OrgId { get; set; }

    [SugarColumn(ColumnDescription = "设备Id")]
    public long DeviceId { get; set; }

    [SugarColumn(ColumnDescription = "设备名称快照", Length = 128)]
    public string DeviceName { get; set; }

    [SugarColumn(ColumnDescription = "告警级别")]
    public IotAlarmSeverityEnum Severity { get; set; }

    [SugarColumn(ColumnDescription = "事件状态")]
    public IotAlarmEventStatusEnum Status { get; set; } = IotAlarmEventStatusEnum.Active;

    [SugarColumn(ColumnDescription = "首次触发时间")]
    public DateTime TriggerTime { get; set; }

    [SugarColumn(ColumnDescription = "最近触发时间")]
    public DateTime LastTriggerTime { get; set; }

    [SugarColumn(ColumnDescription = "恢复时间", IsNullable = true)]
    public DateTime? RecoverTime { get; set; }

    [SugarColumn(ColumnDescription = "确认时间", IsNullable = true)]
    public DateTime? AckTime { get; set; }

    [SugarColumn(ColumnDescription = "闭环时间", IsNullable = true)]
    public DateTime? CloseTime { get; set; }

    [SugarColumn(ColumnDescription = "确认人Id", IsNullable = true)]
    public long? AckUserId { get; set; }

    [SugarColumn(ColumnDescription = "确认人", Length = 64, IsNullable = true)]
    public string AckUserName { get; set; }

    [SugarColumn(ColumnDescription = "闭环人Id", IsNullable = true)]
    public long? CloseUserId { get; set; }

    [SugarColumn(ColumnDescription = "闭环人", Length = 64, IsNullable = true)]
    public string CloseUserName { get; set; }

    [SugarColumn(ColumnDescription = "连续触发累计数")]
    public int TriggerCount { get; set; } = 1;

    [SugarColumn(ColumnDescription = "触发值快照", IsNullable = true, ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string TriggerValue { get; set; }

    [SugarColumn(ColumnDescription = "事件描述", Length = 2048)]
    public string Message { get; set; }

    [SugarColumn(ColumnDescription = "处理备注", Length = 2048, IsNullable = true)]
    public string HandleRemark { get; set; }

    [SugarColumn(ColumnDescription = "最近通知时间", IsNullable = true)]
    public DateTime? LastNotifyTime { get; set; }
}

/// <summary>
/// 告警通知渠道，Secret 只允许服务端使用，API 通过 DTO 隐藏
/// </summary>
[SugarTable("IotAlarmChannel", "告警通知渠道")]
public class IotAlarmChannel : EntityBase
{
    [SugarColumn(ColumnDescription = "渠道名称", Length = 128)]
    public string Name { get; set; }

    [SugarColumn(ColumnDescription = "渠道类型")]
    public IotAlarmChannelTypeEnum Type { get; set; }

    [SugarColumn(ColumnDescription = "是否启用")]
    public bool Enabled { get; set; } = true;

    [SugarColumn(ColumnDescription = "Webhook地址", Length = 2048, IsNullable = true)]
    public string WebhookUrl { get; set; }

    [SugarColumn(ColumnDescription = "SMTP服务器", Length = 256, IsNullable = true)]
    public string Server { get; set; }

    [SugarColumn(ColumnDescription = "SMTP端口", IsNullable = true)]
    public int? Port { get; set; }

    [SugarColumn(ColumnDescription = "启用SSL")]
    public bool UseSsl { get; set; } = true;

    [SugarColumn(ColumnDescription = "账号", Length = 256, IsNullable = true)]
    public string Username { get; set; }

    [SugarColumn(ColumnDescription = "Webhook签名密钥或SMTP密码", Length = 1024, IsNullable = true)]
    public string Secret { get; set; }

    [SugarColumn(ColumnDescription = "发件人", Length = 256, IsNullable = true)]
    public string Sender { get; set; }

    [SugarColumn(ColumnDescription = "收件人，多个以逗号或分号分隔", Length = 2048, IsNullable = true)]
    public string Recipients { get; set; }

    [SugarColumn(ColumnDescription = "备注", Length = 512, IsNullable = true)]
    public string Remark { get; set; }
}

/// <summary>
/// 告警通知审计日志
/// </summary>
[SugarTable("IotAlarmNotificationLog", "告警通知日志")]
[SugarIndex("index_{table}_OrgTime", nameof(OrgId), OrderByType.Asc, nameof(SendTime), OrderByType.Desc)]
public class IotAlarmNotificationLog : EntityBase
{
    [SugarColumn(ColumnDescription = "组织Id快照")]
    public long OrgId { get; set; }

    [SugarColumn(ColumnDescription = "事件Id", IsNullable = true)]
    public long? EventId { get; set; }

    [SugarColumn(ColumnDescription = "规则Id", IsNullable = true)]
    public long? RuleId { get; set; }

    [SugarColumn(ColumnDescription = "渠道Id")]
    public long ChannelId { get; set; }

    [SugarColumn(ColumnDescription = "渠道名称快照", Length = 128)]
    public string ChannelName { get; set; }

    [SugarColumn(ColumnDescription = "渠道类型")]
    public IotAlarmChannelTypeEnum ChannelType { get; set; }

    [SugarColumn(ColumnDescription = "通知类型")]
    public IotAlarmNotificationTypeEnum NotificationType { get; set; }

    [SugarColumn(ColumnDescription = "是否成功")]
    public bool IsSuccess { get; set; }

    [SugarColumn(ColumnDescription = "发送时间")]
    public DateTime SendTime { get; set; }

    [SugarColumn(ColumnDescription = "通知内容", ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Content { get; set; }

    [SugarColumn(ColumnDescription = "响应或错误", IsNullable = true, ColumnDataType = StaticConfig.CodeFirst_BigString)]
    public string Response { get; set; }
}
