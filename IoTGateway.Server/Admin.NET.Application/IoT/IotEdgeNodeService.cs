using Admin.NET.Core;
using Furion;
using Furion.DynamicApiController;
using Furion.FriendlyException;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Admin.NET.Application;

/// <summary>
/// IoT 边缘节点管理（P0 模型：注册/维护，按组织行级隔离）
/// </summary>
[ApiDescriptionSettings(Name = "IotEdgeNode", Order = 100)]
public class IotEdgeNodeService : IDynamicApiController
{
    private readonly SqlSugarRepository<IotEdgeNode> _rep;
    private readonly SqlSugarRepository<SysOrg> _orgRep;
    private readonly SqlSugarRepository<IotDevice> _deviceRep;
    private readonly IotOrgScopeService _orgScope;
    private readonly IIotEdgeDownlink _downlink;

    public IotEdgeNodeService(
        SqlSugarRepository<IotEdgeNode> rep,
        SqlSugarRepository<SysOrg> orgRep,
        SqlSugarRepository<IotDevice> deviceRep,
        IotOrgScopeService orgScope,
        IIotEdgeDownlink downlink)
    {
        _rep = rep;
        _orgRep = orgRep;
        _deviceRep = deviceRep;
        _orgScope = orgScope;
        _downlink = downlink;
    }

    /// <summary>
    /// 构造当前用户可见的边缘节点查询（按组织范围过滤）
    /// </summary>
    private async Task<ISugarQueryable<IotEdgeNode>> ScopedQuery(long? orgId = null)
    {
        var scope = await _orgScope.GetOrgFilterAsync(orgId);
        var query = _rep.AsQueryable();
        if (scope.IsUnrestricted) return query;
        if (scope.OrgIds.Count == 0) return query.Where(u => u.Id == 0);
        return query.Where(u => scope.OrgIds.Contains(u.OrgId));
    }

    private async Task FillOrgNames(IEnumerable<IotEdgeNode> items)
    {
        var list = items?.ToList();
        if (list == null || list.Count == 0) return;
        var orgIds = list.Select(u => u.OrgId).Where(u => u > 0).Distinct().ToList();
        if (orgIds.Count == 0) return;
        var names = await _orgRep.AsQueryable().Where(u => orgIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Name }).ToListAsync();
        var map = names.ToDictionary(u => u.Id, u => u.Name);
        foreach (var it in list)
            it.OrgName = map.TryGetValue(it.OrgId, out var n) ? n : null;
    }

    [DisplayName("边缘节点分页"), HttpPost]
    public async Task<SqlSugarPagedList<IotEdgeNode>> Page([FromBody] IotEdgeNodePageInput input)
    {
        var query = await ScopedQuery(input?.OrgId);
        if (!string.IsNullOrWhiteSpace(input?.Keyword))
            query = query.Where(u => u.Name.Contains(input.Keyword) || u.NodeCode.Contains(input.Keyword));
        if (input?.Online.HasValue == true)
            query = query.Where(u => u.Online == input.Online.Value);

        var page = await query.OrderBy(u => u.CreateTime, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
        await FillOrgNames(page.Items);
        return page;
    }

    [DisplayName("边缘节点列表"), HttpPost]
    public async Task<List<IotEdgeNode>> List([FromBody] IotEdgeNodePageInput input)
    {
        var query = await ScopedQuery(input?.OrgId);
        if (!string.IsNullOrWhiteSpace(input?.Keyword))
            query = query.Where(u => u.Name.Contains(input.Keyword) || u.NodeCode.Contains(input.Keyword));
        var list = await query.OrderBy(u => u.Name).ToListAsync();
        await FillOrgNames(list);
        return list;
    }

    [DisplayName("边缘节点详情"), HttpGet]
    public async Task<IotEdgeNode> Detail([FromQuery] long id)
    {
        var entity = await (await ScopedQuery()).FirstAsync(u => u.Id == id);
        return entity ?? throw Oops.Oh("边缘节点不存在或无权访问");
    }

    /// <summary>
    /// 边缘节点心跳上报（匿名，按节点编号寻址；用于 HTTP 方式保活）
    /// </summary>
    [DisplayName("边缘节点心跳"), ApiDescriptionSettings(Name = "Heartbeat"), HttpPost]
    [AllowAnonymous]
    public async Task Heartbeat([FromBody] IotEdgeNodeHeartbeatInput input)
    {
        if (input == null || string.IsNullOrWhiteSpace(input.NodeCode))
            throw Oops.Oh("节点编号不能为空");
        var node = await _rep.AsQueryable().FirstAsync(u => u.NodeCode == input.NodeCode);
        if (node == null) throw Oops.Oh("边缘节点不存在");
        if (!node.Enabled) throw Oops.Oh("边缘节点已禁用");
        ValidateAccessKey(node.AccessKey, input.AccessKey);
        var now = DateTime.UtcNow;
        var version = string.IsNullOrWhiteSpace(input.Version) ? node.Version : input.Version;
        await _rep.AsUpdateable()
            .SetColumns(u => u.Online == true)
            .SetColumns(u => u.LastHeartbeatTime == now)
            .SetColumns(u => u.Version == version)
            .SetColumns(u => u.UpdateTime == now)
            .Where(u => u.Id == node.Id)
            .ExecuteCommandAsync();
    }

    /// <summary>
    /// 重置边缘节点接入密钥（返回明文，仅此一次）
    /// </summary>
    [DisplayName("重置接入密钥"), ApiDescriptionSettings(Name = "RegenerateKey"), HttpPost]
    public async Task<object> RegenerateKey([FromBody] IotIdInput input)
    {
        if (input == null || input.Id <= 0) throw Oops.Oh("id不能为空");
        var entity = await (await ScopedQuery()).FirstAsync(u => u.Id == input.Id)
            ?? throw Oops.Oh("边缘节点不存在或无权访问");
        var key = GenerateAccessKey();
        await _rep.AsUpdateable()
            .SetColumns(u => u.AccessKey == key)
            .SetColumns(u => u.UpdateTime == DateTime.UtcNow)
            .Where(u => u.Id == entity.Id)
            .ExecuteCommandAsync();
        return new { accessKey = key };
    }

    /// <summary>
    /// 边缘节点下发（配置/指令，经 MQTT 主题 edge/{nodeCode}/{kind}）
    /// </summary>
    [DisplayName("边缘节点下发"), ApiDescriptionSettings(Name = "Send"), HttpPost]
    public async Task<object> Send([FromBody] IotEdgeNodeSendInput input)
    {
        if (input == null || input.Id <= 0) throw Oops.Oh("节点Id不能为空");
        var entity = await (await ScopedQuery()).FirstAsync(u => u.Id == input.Id)
            ?? throw Oops.Oh("边缘节点不存在或无权访问");
        if (!entity.Enabled) throw Oops.Oh("边缘节点已禁用");
        if (string.IsNullOrWhiteSpace(input.Payload)) throw Oops.Oh("下发内容不能为空");

        var kind = string.IsNullOrWhiteSpace(input.Kind) ? "config" : input.Kind.Trim().ToLowerInvariant();
        if (kind != "config" && kind != "command") throw Oops.Oh("下发类型仅支持 config 或 command");
        if (!_downlink.IsRunning) throw Oops.Oh("MQTT 服务未运行，无法下发");

        var topic = $"edge/{entity.NodeCode}/{kind}";
        var retain = kind == "config";
        await _downlink.PublishAsync(topic, input.Payload, retain);
        return new { topic, retain, online = entity.Online ?? false };
    }

    [DisplayName("新增边缘节点"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotEdgeNode input)
    {
        input.Id = 0;
        input.OrgId = await _orgScope.ResolveOrgIdAsync(input.OrgId);
        Validate(input);
        if (await _rep.AsQueryable().AnyAsync(u => u.NodeCode == input.NodeCode))
            throw Oops.Oh("节点编号已存在");
        input.AccessKey = GenerateAccessKey();
        await _rep.InsertAsync(input);
        return input.Id;
    }

    [DisplayName("更新边缘节点"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotEdgeNode input)
    {
        var entity = await (await ScopedQuery()).FirstAsync(u => u.Id == input.Id)
            ?? throw Oops.Oh("边缘节点不存在或无权访问");
        await _orgScope.EnsureOrgAccessAsync(entity.OrgId);
        input.OrgId = input.OrgId > 0 ? await _orgScope.ResolveOrgIdAsync(input.OrgId) : entity.OrgId;
        Validate(input);
        if (await _rep.AsQueryable().AnyAsync(u => u.NodeCode == input.NodeCode && u.Id != input.Id))
            throw Oops.Oh("节点编号已存在");
        input.CreateTime = entity.CreateTime;
        input.AccessKey = entity.AccessKey; // 密钥不随编辑变更，需走重置密钥
        await _rep.UpdateAsync(input);
    }

    [DisplayName("删除边缘节点"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        if (input == null || input.Id <= 0) throw Oops.Oh("id不能为空");
        await DeleteCore(new List<long> { input.Id });
    }

    [DisplayName("批量删除边缘节点"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) throw Oops.Oh("ids不能为空");
        await DeleteCore(input.Ids);
    }

    private async Task DeleteCore(List<long> ids)
    {
        var list = await (await ScopedQuery()).Where(u => ids.Contains(u.Id)).ToListAsync();
        if (list.Count != ids.Count) throw Oops.Oh("部分边缘节点不存在或无权访问");
        foreach (var id in ids)
        {
            if (await _deviceRep.AsQueryable().AnyAsync(u => u.EdgeNodeId == id))
                throw Oops.Oh("存在已绑定设备的边缘节点，无法删除");
        }
        await _rep.DeleteAsync(list);
    }

    private static void Validate(IotEdgeNode input)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw Oops.Oh("节点名称不能为空");
        if (string.IsNullOrWhiteSpace(input.NodeCode)) throw Oops.Oh("节点编号不能为空");
        if (input.NodeCode.Length > 128) throw Oops.Oh("节点编号长度不能超过128");
    }

    /// <summary>生成 32 位十六进制接入密钥</summary>
    private static string GenerateAccessKey() => Guid.NewGuid().ToString("N");

    /// <summary>校验接入密钥（定长比较，防时序侧信道）</summary>
    private static void ValidateAccessKey(string expected, string provided)
    {
        if (string.IsNullOrWhiteSpace(expected))
            throw Oops.Oh("该节点尚未配置接入密钥，请先在边缘节点管理重置密钥");
        if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(expected, provided))
            throw Oops.Oh("接入密钥错误");
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
