using System.ComponentModel.DataAnnotations;

namespace Admin.NET.Application;

public class IotAlarmRulePageInput : BasePageInput
{
    public new string Keyword { get; set; }
    public long? DeviceId { get; set; }
    public long? OrgId { get; set; }
    public IotAlarmSeverityEnum? Severity { get; set; }
    public bool? Enabled { get; set; }
}

public class IotAlarmEventPageInput : BasePageInput
{
    public new string Keyword { get; set; }
    public long? DeviceId { get; set; }
    public long? OrgId { get; set; }
    public IotAlarmSeverityEnum? Severity { get; set; }
    public IotAlarmEventStatusEnum? Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public class IotAlarmEventHandleInput
{
    [Required(ErrorMessage = "告警事件Id不能为空")]
    public long Id { get; set; }

    [StringLength(2048, ErrorMessage = "处理备注不能超过2048个字符")]
    public string Remark { get; set; }
}

public class IotAlarmEventListInput
{
    [Required(ErrorMessage = "设备Id不能为空")]
    public long DeviceId { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public class IotAlarmOverviewDto
{
    public int ActiveCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public int TodayAlarmCount { get; set; }
    public int TodayRecoveredCount { get; set; }
    public List<IotAlarmChartPointDto> SeverityDistribution { get; set; } = new();
    public List<IotAlarmChartPointDto> HourlyTrend { get; set; } = new();
    public List<IotAlarmEvent> LatestEvents { get; set; } = new();
}

public class IotAlarmChartPointDto
{
    public string Category { get; set; }
    public int Value { get; set; }
}

public class IotAlarmChannelDto
{
    public long Id { get; set; }
    public string Name { get; set; }
    public IotAlarmChannelTypeEnum Type { get; set; }
    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; }
    public string Server { get; set; }
    public int? Port { get; set; }
    public bool UseSsl { get; set; }
    public string Username { get; set; }
    public bool HasSecret { get; set; }
    public string Sender { get; set; }
    public string Recipients { get; set; }
    public string Remark { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime? UpdateTime { get; set; }
}

public class IotAlarmChannelInput
{
    public long Id { get; set; }

    [Required(ErrorMessage = "渠道名称不能为空")]
    [StringLength(128)]
    public string Name { get; set; }

    public IotAlarmChannelTypeEnum Type { get; set; }
    public bool Enabled { get; set; } = true;
    public string WebhookUrl { get; set; }
    public string Server { get; set; }
    public int? Port { get; set; }
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; }

    /// <summary>
    /// 新增时写入；修改时传 null 表示保留原密钥，传空字符串表示清除
    /// </summary>
    public string Secret { get; set; }
    public string Sender { get; set; }
    public string Recipients { get; set; }
    public string Remark { get; set; }
}

public class IotAlarmNotificationPageInput : BasePageInput
{
    public long? EventId { get; set; }
    public long? ChannelId { get; set; }
    public long? OrgId { get; set; }
    public bool? IsSuccess { get; set; }
}
