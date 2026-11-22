using System.ComponentModel;
using Admin.NET.Core.Service;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 告警事件、处理闭环与告警大屏
/// </summary>
[ApiDescriptionSettings(Order = 459)]
public class IotAlarmEventService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotAlarmEvent> _eventRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly UserManager _userManager;

    public IotAlarmEventService(SqlSugarRepository<IotAlarmEvent> eventRep, IotOrgScopeService orgScope, UserManager userManager)
    {
        _eventRep = eventRep;
        _orgScope = orgScope;
        _userManager = userManager;
    }

    [DisplayName("告警事件分页"), HttpPost]
    public async Task<SqlSugarPagedList<IotAlarmEvent>> Page([FromBody] IotAlarmEventPageInput input)
    {
        if (input.DeviceId.HasValue) await _orgScope.EnsureDeviceAccessAsync(input.DeviceId.Value);
        return await (await EventQueryAsync(input.OrgId))
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.RuleName.Contains(input.Keyword) || u.DeviceName.Contains(input.Keyword) || u.Message.Contains(input.Keyword))
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(input.Severity.HasValue, u => u.Severity == input.Severity)
            .WhereIF(input.Status.HasValue, u => u.Status == input.Status)
            .WhereIF(input.StartTime.HasValue, u => u.TriggerTime >= input.StartTime)
            .WhereIF(input.EndTime.HasValue, u => u.TriggerTime <= input.EndTime)
            .OrderBy(u => u.LastTriggerTime, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    [DisplayName("告警事件详情"), HttpGet]
    public async Task<IotAlarmEvent> Detail([FromQuery] long id) => await EnsureEventAccessAsync(id);

    [DisplayName("设备告警事件列表"), HttpGet]
    public async Task<List<IotAlarmEvent>> List([FromQuery] IotAlarmEventListInput input)
    {
        await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
        return await (await EventQueryAsync())
            .Where(u => u.DeviceId == input.DeviceId)
            .WhereIF(input.StartTime.HasValue, u => u.TriggerTime >= input.StartTime)
            .WhereIF(input.EndTime.HasValue, u => u.TriggerTime <= input.EndTime)
            .OrderBy(u => u.TriggerTime, OrderByType.Desc)
            .Take(5000)
            .ToListAsync();
    }

    [DisplayName("确认告警"), ApiDescriptionSettings(Name = "Ack"), HttpPost]
    public async Task Ack([FromBody] IotAlarmEventHandleInput input)
    {
        var alarmEvent = await EnsureEventAccessAsync(input.Id);
        if (alarmEvent.Status != IotAlarmEventStatusEnum.Active) throw Oops.Oh("仅活动中的告警可以确认");
        var now = DateTime.Now;
        var rows = await _eventRep.AsUpdateable()
            .SetColumns(u => new IotAlarmEvent
            {
                Status = IotAlarmEventStatusEnum.Acknowledged,
                AckTime = now,
                AckUserId = _userManager.UserId,
                AckUserName = _userManager.RealName,
                HandleRemark = input.Remark,
                UpdateTime = now,
                UpdateUserId = _userManager.UserId,
                UpdateUserName = _userManager.RealName
            })
            .Where(u => u.Id == input.Id && u.Status == IotAlarmEventStatusEnum.Active)
            .ExecuteCommandAsync();
        if (rows == 0) throw Oops.Oh("告警状态已变化，请刷新后重试");
    }

    [DisplayName("闭环告警"), ApiDescriptionSettings(Name = "Close"), HttpPost]
    public async Task Close([FromBody] IotAlarmEventHandleInput input)
    {
        var alarmEvent = await EnsureEventAccessAsync(input.Id);
        if (alarmEvent.Status != IotAlarmEventStatusEnum.Recovered) throw Oops.Oh("仅已恢复的告警可以闭环");
        var now = DateTime.Now;
        var rows = await _eventRep.AsUpdateable()
            .SetColumns(u => new IotAlarmEvent
            {
                Status = IotAlarmEventStatusEnum.Closed,
                CloseTime = now,
                CloseUserId = _userManager.UserId,
                CloseUserName = _userManager.RealName,
                HandleRemark = input.Remark,
                UpdateTime = now,
                UpdateUserId = _userManager.UserId,
                UpdateUserName = _userManager.RealName
            })
            .Where(u => u.Id == input.Id && u.Status == IotAlarmEventStatusEnum.Recovered)
            .ExecuteCommandAsync();
        if (rows == 0) throw Oops.Oh("告警状态已变化，请刷新后重试");
    }

    [DisplayName("告警大屏统计"), HttpGet]
    public async Task<IotAlarmOverviewDto> Overview([FromQuery] long? orgId = null)
    {
        var today = DateTime.Today;
        var hourStart = DateTime.Now.AddHours(-23);
        hourStart = new DateTime(hourStart.Year, hourStart.Month, hourStart.Day, hourStart.Hour, 0, 0);
        var activeEvents = await (await EventQueryAsync(orgId))
            .Where(u => u.Status == IotAlarmEventStatusEnum.Active || u.Status == IotAlarmEventStatusEnum.Acknowledged)
            .ToListAsync();
        var todayEvents = await (await EventQueryAsync(orgId))
            .Where(u => u.TriggerTime >= today || (u.RecoverTime.HasValue && u.RecoverTime >= today))
            .ToListAsync();
        var hourlyEvents = todayEvents.Where(u => u.TriggerTime >= hourStart).ToList();
        var latest = await (await EventQueryAsync(orgId))
            .OrderBy(u => u.LastTriggerTime, OrderByType.Desc)
            .Take(8)
            .ToListAsync();

        var result = new IotAlarmOverviewDto
        {
            ActiveCount = activeEvents.Count(u => u.Status == IotAlarmEventStatusEnum.Active),
            AcknowledgedCount = activeEvents.Count(u => u.Status == IotAlarmEventStatusEnum.Acknowledged),
            TodayAlarmCount = todayEvents.Count(u => u.TriggerTime >= today),
            TodayRecoveredCount = todayEvents.Count(u => u.RecoverTime >= today),
            LatestEvents = latest
        };
        foreach (var severity in Enum.GetValues<IotAlarmSeverityEnum>())
        {
            result.SeverityDistribution.Add(new IotAlarmChartPointDto
            {
                Category = severity.ToString(),
                Value = todayEvents.Count(u => u.TriggerTime >= today && u.Severity == severity)
            });
        }
        for (var i = 0; i < 24; i++)
        {
            var hour = hourStart.AddHours(i);
            var next = hour.AddHours(1);
            result.HourlyTrend.Add(new IotAlarmChartPointDto
            {
                Category = hour.ToString("HH:mm"),
                Value = hourlyEvents.Count(u => u.TriggerTime >= hour && u.TriggerTime < next)
            });
        }
        return result;
    }

    private async Task<IotAlarmEvent> EnsureEventAccessAsync(long id)
    {
        return await (await EventQueryAsync()).FirstAsync(u => u.Id == id) ?? throw Oops.Oh("告警事件不存在或无权访问");
    }

    private async Task<ISugarQueryable<IotAlarmEvent>> EventQueryAsync(long? orgId = null)
    {
        var scope = await _orgScope.GetOrgFilterAsync(orgId);
        return _eventRep.AsQueryable()
            .WhereIF(!scope.IsUnrestricted && scope.OrgIds.Count > 0, u => scope.OrgIds.Contains(u.OrgId))
            .WhereIF(!scope.IsUnrestricted && scope.OrgIds.Count == 0, u => u.Id == 0);
    }
}
