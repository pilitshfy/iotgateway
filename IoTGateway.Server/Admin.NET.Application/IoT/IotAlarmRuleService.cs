using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 告警规则管理
/// </summary>
[ApiDescriptionSettings(Order = 458)]
public class IotAlarmRuleService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotAlarmRule> _ruleRep;
    private readonly SqlSugarRepository<IotAlarmCondition> _conditionRep;
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly IServiceProvider _serviceProvider;

    public IotAlarmRuleService(
        SqlSugarRepository<IotAlarmRule> ruleRep,
        SqlSugarRepository<IotAlarmCondition> conditionRep,
        SqlSugarRepository<IotDeviceVariable> variableRep,
        IotOrgScopeService orgScope,
        IServiceProvider serviceProvider)
    {
        _ruleRep = ruleRep;
        _conditionRep = conditionRep;
        _variableRep = variableRep;
        _orgScope = orgScope;
        _serviceProvider = serviceProvider;
    }

    private IIotAlarmRuntime Runtime => _serviceProvider.GetService<IIotAlarmRuntime>();

    [DisplayName("告警规则分页"), HttpPost]
    public async Task<SqlSugarPagedList<IotAlarmRule>> Page([FromBody] IotAlarmRulePageInput input)
    {
        var query = await RuleQueryAsync(input.OrgId);
        var result = await query
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.Name.Contains(input.Keyword))
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(input.Severity.HasValue, u => u.Severity == input.Severity)
            .WhereIF(input.Enabled.HasValue, u => u.Enabled == input.Enabled)
            .OrderBy(u => u.Id, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
        await FillMetadataAsync(result.Items);
        return result;
    }

    [DisplayName("告警规则详情"), HttpGet]
    public async Task<IotAlarmRule> Detail([FromQuery] long id)
    {
        var rule = await EnsureRuleAccessAsync(id);
        await FillMetadataAsync(new[] { rule });
        return rule;
    }

    [DisplayName("新增告警规则"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotAlarmRule input)
    {
        var device = await ValidateRuleAsync(input);
        input.Id = 0;
        input.OrgId = device.OrgId;
        var db = _ruleRep.Context;
        db.Ado.BeginTran();
        try
        {
            await _ruleRep.InsertAsync(input);
            foreach (var condition in input.Conditions)
            {
                condition.Id = 0;
                condition.RuleId = input.Id;
                await _conditionRep.InsertAsync(condition);
            }
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        if (Runtime != null) await Runtime.ReloadAsync();
        return input.Id;
    }

    [DisplayName("修改告警规则"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotAlarmRule input)
    {
        var existing = await EnsureRuleAccessAsync(input.Id);
        var device = await ValidateRuleAsync(input);
        if (existing.DeviceId != input.DeviceId && await _ruleRep.Context.Queryable<IotAlarmEvent>()
                .AnyAsync(u => u.RuleId == input.Id && (u.Status == IotAlarmEventStatusEnum.Active || u.Status == IotAlarmEventStatusEnum.Acknowledged)))
            throw Oops.Oh("规则存在活动告警，恢复后才能更换设备");

        input.OrgId = device.OrgId;
        input.CreateTime = existing.CreateTime;
        input.CreateUserId = existing.CreateUserId;
        input.CreateUserName = existing.CreateUserName;
        var db = _ruleRep.Context;
        db.Ado.BeginTran();
        try
        {
            await _ruleRep.AsUpdateable(input).ExecuteCommandAsync();
            await _conditionRep.Context.Deleteable<IotAlarmCondition>().Where(u => u.RuleId == input.Id).ExecuteCommandAsync();
            foreach (var condition in input.Conditions)
            {
                condition.Id = 0;
                condition.RuleId = input.Id;
                await _conditionRep.InsertAsync(condition);
            }
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        if (Runtime != null) await Runtime.ReloadAsync();
    }

    [DisplayName("删除告警规则"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        await EnsureRuleAccessAsync(input.Id);
        var db = _ruleRep.Context;
        db.Ado.BeginTran();
        try
        {
            await _conditionRep.Context.Deleteable<IotAlarmCondition>().Where(u => u.RuleId == input.Id).ExecuteCommandAsync();
            await _ruleRep.DeleteByIdAsync(input.Id);
            db.Ado.CommitTran();
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        // 删除后的遗留活动事件由引擎自动恢复，历史事件保留。
        if (Runtime != null) await Runtime.ReloadAsync();
    }

    private async Task<IotAlarmRule> EnsureRuleAccessAsync(long id)
    {
        var rule = await _ruleRep.GetByIdAsync(id) ?? throw Oops.Oh("告警规则不存在");
        await _orgScope.EnsureDeviceAccessAsync(rule.DeviceId);
        return rule;
    }

    private async Task<IotDevice> ValidateRuleAsync(IotAlarmRule input)
    {
        if (input == null) throw Oops.Oh("告警规则不能为空");
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 128) throw Oops.Oh("规则名称不能为空且不能超过128个字符");
        if (input.TriggerSamples < 1 || input.RecoverySamples < 1) throw Oops.Oh("连续触发和连续恢复样本数必须大于0");
        if (input.SuppressSeconds < 0) throw Oops.Oh("重复通知间隔不能小于0");
        if (input.Conditions == null || input.Conditions.Count == 0) throw Oops.Oh("至少配置一个告警条件");

        var device = await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
        if (device.DeviceTypeEnum != IotDeviceTypeEnum.Device) throw Oops.Oh("告警规则只能绑定采集设备");
        var variableIds = input.Conditions.Select(u => u.VariableId).Distinct().ToList();
        if (variableIds.Any(u => u <= 0) || await _variableRep.AsQueryable().CountAsync(u => u.DeviceId == device.Id && variableIds.Contains(u.Id)) != variableIds.Count)
            throw Oops.Oh("告警条件包含不存在或不属于该设备的变量");

        foreach (var condition in input.Conditions)
        {
            if (condition.Operator == IotAlarmOperatorEnum.StatusEqual || condition.Operator == IotAlarmOperatorEnum.StatusNotEqual)
            {
                if (!condition.Status.HasValue) throw Oops.Oh("状态比较必须选择变量状态");
                condition.Value = null;
                condition.SecondValue = null;
                continue;
            }
            if (!condition.Value.HasValue) throw Oops.Oh("数值比较必须填写阈值");
            if ((condition.Operator == IotAlarmOperatorEnum.Between || condition.Operator == IotAlarmOperatorEnum.Outside) && !condition.SecondValue.HasValue)
                throw Oops.Oh("区间比较必须填写上限和下限");
            condition.Status = null;
        }

        input.ChannelIds = input.ChannelIds?.Where(u => u > 0).Distinct().ToList() ?? new List<long>();
        if (input.ChannelIds.Count > 0)
        {
            var channelCount = await _ruleRep.Context.Queryable<IotAlarmChannel>().CountAsync(u => input.ChannelIds.Contains(u.Id));
            if (channelCount != input.ChannelIds.Count) throw Oops.Oh("部分通知渠道不存在");
        }
        return device;
    }

    private async Task<ISugarQueryable<IotAlarmRule>> RuleQueryAsync(long? orgId)
    {
        var scope = await _orgScope.GetOrgFilterAsync(orgId);
        return _ruleRep.AsQueryable()
            .WhereIF(scope.IsUnrestricted == false && scope.OrgIds.Count > 0, u => scope.OrgIds.Contains(u.OrgId))
            .WhereIF(scope.IsUnrestricted == false && scope.OrgIds.Count == 0, u => u.Id == 0);
    }

    private async Task FillMetadataAsync(IEnumerable<IotAlarmRule> rules)
    {
        var list = rules?.ToList() ?? new List<IotAlarmRule>();
        if (list.Count == 0) return;
        var ruleIds = list.Select(u => u.Id).ToList();
        var deviceIds = list.Select(u => u.DeviceId).Distinct().ToList();
        var orgIds = list.Select(u => u.OrgId).Distinct().ToList();
        var conditions = await _conditionRep.AsQueryable().Where(u => ruleIds.Contains(u.RuleId)).OrderBy(u => u.Sort).ToListAsync();
        var variableIds = conditions.Select(u => u.VariableId).Distinct().ToList();
        var variables = variableIds.Count == 0 ? new List<IotDeviceVariable>() : await _variableRep.AsQueryable().Where(u => variableIds.Contains(u.Id)).ToListAsync();
        var devices = await _ruleRep.Context.Queryable<IotDevice>().Where(u => deviceIds.Contains(u.Id)).ToListAsync();
        var orgs = await _ruleRep.Context.Queryable<SysOrg>().Where(u => orgIds.Contains(u.Id)).ToListAsync();
        foreach (var rule in list)
        {
            rule.DeviceName = devices.FirstOrDefault(u => u.Id == rule.DeviceId)?.DeviceName;
            rule.OrgName = orgs.FirstOrDefault(u => u.Id == rule.OrgId)?.Name;
            rule.Conditions = conditions.Where(u => u.RuleId == rule.Id).ToList();
            foreach (var condition in rule.Conditions)
                condition.VariableName = variables.FirstOrDefault(u => u.Id == condition.VariableId)?.Name;
        }
    }
}
