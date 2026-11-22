using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 告警通知渠道与发送审计，渠道密钥仅允许服务端读取
/// </summary>
[ApiDescriptionSettings(Order = 460)]
public class IotAlarmChannelService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotAlarmChannel> _channelRep;
    private readonly SqlSugarRepository<IotAlarmNotificationLog> _logRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly IServiceProvider _serviceProvider;

    public IotAlarmChannelService(
        SqlSugarRepository<IotAlarmChannel> channelRep,
        SqlSugarRepository<IotAlarmNotificationLog> logRep,
        IotOrgScopeService orgScope,
        IServiceProvider serviceProvider)
    {
        _channelRep = channelRep;
        _logRep = logRep;
        _orgScope = orgScope;
        _serviceProvider = serviceProvider;
    }

    private IIotAlarmNotifier Notifier => _serviceProvider.GetService<IIotAlarmNotifier>();

    [DisplayName("通知渠道分页"), HttpPost]
    public async Task<SqlSugarPagedList<IotAlarmChannelDto>> Page([FromBody] BasePageInput input)
    {
        _orgScope.EnsureSuperAdmin();
        var page = await _channelRep.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.Name.Contains(input.Keyword))
            .OrderBy(u => u.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
        return new SqlSugarPagedList<IotAlarmChannelDto>
        {
            Page = page.Page,
            PageSize = page.PageSize,
            Total = page.Total,
            TotalPages = page.TotalPages,
            Items = page.Items.Select(ToDto).ToList()
        };
    }

    /// <summary>
    /// 规则编辑器使用的安全选项，不返回地址、收件人或密钥
    /// </summary>
    [DisplayName("通知渠道选项"), HttpGet]
    public async Task<object> Options()
    {
        return await _channelRep.AsQueryable()
            .OrderBy(u => u.Name)
            .Select(u => new { u.Id, u.Name, u.Type, u.Enabled })
            .ToListAsync();
    }

    [DisplayName("新增通知渠道"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotAlarmChannelInput input)
    {
        _orgScope.EnsureSuperAdmin();
        Validate(input);
        var entity = new IotAlarmChannel
        {
            Name = input.Name.Trim(),
            Type = input.Type,
            Enabled = input.Enabled,
            WebhookUrl = input.WebhookUrl?.Trim(),
            Server = input.Server?.Trim(),
            Port = input.Port,
            UseSsl = input.UseSsl,
            Username = input.Username,
            Secret = input.Secret,
            Sender = input.Sender,
            Recipients = input.Recipients,
            Remark = input.Remark
        };
        await _channelRep.InsertAsync(entity);
        return entity.Id;
    }

    [DisplayName("修改通知渠道"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotAlarmChannelInput input)
    {
        _orgScope.EnsureSuperAdmin();
        Validate(input);
        var entity = await _channelRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("通知渠道不存在");
        entity.Name = input.Name.Trim();
        entity.Type = input.Type;
        entity.Enabled = input.Enabled;
        entity.WebhookUrl = input.WebhookUrl?.Trim();
        entity.Server = input.Server?.Trim();
        entity.Port = input.Port;
        entity.UseSsl = input.UseSsl;
        entity.Username = input.Username;
        if (input.Secret != null) entity.Secret = input.Secret;
        entity.Sender = input.Sender;
        entity.Recipients = input.Recipients;
        entity.Remark = input.Remark;
        await _channelRep.AsUpdateable(entity).ExecuteCommandAsync();
    }

    [DisplayName("删除通知渠道"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        _orgScope.EnsureSuperAdmin();
        await _channelRep.DeleteByIdAsync(input.Id);
    }

    [DisplayName("测试通知渠道"), ApiDescriptionSettings(Name = "Test"), HttpPost]
    public async Task<IotAlarmNotificationResult> Test([FromBody] IotIdInput input)
    {
        _orgScope.EnsureSuperAdmin();
        if (Notifier == null) throw Oops.Oh("通知发送器未启动");
        var channel = await _channelRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("通知渠道不存在");
        var testEvent = new IotAlarmEvent
        {
            RuleName = "渠道连通性测试",
            DeviceName = "IoTGateway",
            OrgId = SqlSugarConst.DefaultTenantId,
            TriggerTime = DateTime.Now,
            LastTriggerTime = DateTime.Now,
            Message = "这是一条测试消息"
        };
        var result = await Notifier.SendAsync(channel, testEvent, IotAlarmNotificationTypeEnum.Test);
        await _logRep.InsertAsync(new IotAlarmNotificationLog
        {
            OrgId = testEvent.OrgId,
            RuleId = null,
            EventId = null,
            ChannelId = channel.Id,
            ChannelName = channel.Name,
            ChannelType = channel.Type,
            NotificationType = IotAlarmNotificationTypeEnum.Test,
            IsSuccess = result.IsSuccess,
            SendTime = DateTime.Now,
            Content = result.Content,
            Response = result.Response,
            CreateTime = DateTime.Now
        });
        return result;
    }

    [DisplayName("通知日志分页"), ApiDescriptionSettings(Name = "NotificationPage"), HttpPost]
    public async Task<SqlSugarPagedList<IotAlarmNotificationLog>> NotificationPage([FromBody] IotAlarmNotificationPageInput input)
    {
        var scope = await _orgScope.GetOrgFilterAsync(input.OrgId);
        return await _logRep.AsQueryable()
            .WhereIF(!scope.IsUnrestricted && scope.OrgIds.Count > 0, u => scope.OrgIds.Contains(u.OrgId))
            .WhereIF(!scope.IsUnrestricted && scope.OrgIds.Count == 0, u => u.Id == 0)
            .WhereIF(input.EventId.HasValue, u => u.EventId == input.EventId)
            .WhereIF(input.ChannelId.HasValue, u => u.ChannelId == input.ChannelId)
            .WhereIF(input.IsSuccess.HasValue, u => u.IsSuccess == input.IsSuccess)
            .OrderBy(u => u.SendTime, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    private static void Validate(IotAlarmChannelInput input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 128) throw Oops.Oh("渠道名称不能为空且不能超过128个字符");
        if (input.Type == IotAlarmChannelTypeEnum.Email)
        {
            if (string.IsNullOrWhiteSpace(input.Server) || input.Port is < 1 or > 65535) throw Oops.Oh("请填写有效的 SMTP 服务器和端口");
            if (string.IsNullOrWhiteSpace(input.Sender) || string.IsNullOrWhiteSpace(input.Recipients)) throw Oops.Oh("请填写发件人和收件人");
        }
        else
        {
            if (!Uri.TryCreate(input.WebhookUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw Oops.Oh("请填写有效的 HTTP(S) Webhook 地址");
        }
    }

    private static IotAlarmChannelDto ToDto(IotAlarmChannel entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        Type = entity.Type,
        Enabled = entity.Enabled,
        WebhookUrl = entity.WebhookUrl,
        Server = entity.Server,
        Port = entity.Port,
        UseSsl = entity.UseSsl,
        Username = entity.Username,
        HasSecret = !string.IsNullOrEmpty(entity.Secret),
        Sender = entity.Sender,
        Recipients = entity.Recipients,
        Remark = entity.Remark,
        CreateTime = entity.CreateTime,
        UpdateTime = entity.UpdateTime
    };
}
