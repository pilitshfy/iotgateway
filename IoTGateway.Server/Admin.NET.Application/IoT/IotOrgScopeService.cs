using Admin.NET.Core.Service;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// 当前用户的 IoT 组织数据范围
/// </summary>
public sealed class IotOrgScope
{
    public bool IsUnrestricted { get; init; }

    public List<long> OrgIds { get; init; } = new();
}

/// <summary>
/// 设备组织字段的幂等迁移（CodeFirst 执行后调用）
/// </summary>
public static class IotOrgSchemaMigration
{
    public static void Migrate()
    {
        var db = SqlSugarSetup.ITenant.GetConnectionScope(SqlSugarConst.MainConfigId);
        var entity = db.EntityMaintenance.GetEntityInfo<IotDevice>();
        var orgColumn = entity.Columns.First(u => u.PropertyName == nameof(IotDevice.OrgId)).DbColumnName;
        var rootOrgId = SqlSugarConst.DefaultTenantId;

        db.Updateable<IotDevice>().SetColumns(u => u.OrgId == rootOrgId).Where(u => u.OrgId == 0).ExecuteCommand();
        if (db.CurrentConnectionConfig.DbType == DbType.PostgreSQL)
        {
            var tableName = db.DbMaintenance.GetTableInfoList(false)
                .First(u => string.Equals(u.Name, entity.DbTableName, StringComparison.OrdinalIgnoreCase)).Name;
            var columnName = db.DbMaintenance.GetColumnInfosByTableName(tableName, false)
                .First(u => string.Equals(u.DbColumnName, orgColumn, StringComparison.OrdinalIgnoreCase)).DbColumnName;
            db.Ado.ExecuteCommand($"ALTER TABLE \"{tableName}\" ALTER COLUMN \"{columnName}\" SET DEFAULT {rootOrgId}");
        }
    }
}

/// <summary>
/// IoT 组织数据范围解析与设备访问校验
/// </summary>
public class IotOrgScopeService : ITransient
{
    private readonly UserManager _userManager;
    private readonly SysOrgService _sysOrgService;
    private readonly SqlSugarRepository<IotDevice> _deviceRep;
    private readonly SqlSugarRepository<SysOrg> _orgRep;

    public IotOrgScopeService(
        UserManager userManager,
        SysOrgService sysOrgService,
        SqlSugarRepository<IotDevice> deviceRep,
        SqlSugarRepository<SysOrg> orgRep)
    {
        _userManager = userManager;
        _sysOrgService = sysOrgService;
        _deviceRep = deviceRep;
        _orgRep = orgRep;
    }

    /// <summary>
    /// 超级管理员不受限；其他用户严格按 Admin.NET 组织数据范围过滤
    /// </summary>
    public async Task<IotOrgScope> GetScopeAsync()
    {
        if (_userManager.SuperAdmin) return new IotOrgScope { IsUnrestricted = true };

        var orgIds = await _sysOrgService.GetUserOrgIdList();
        return new IotOrgScope
        {
            OrgIds = orgIds.Where(u => u > 0).Distinct().ToList()
        };
    }

    /// <summary>
    /// 构造当前用户可见设备查询，可附加组织及其子组织筛选
    /// </summary>
    public async Task<ISugarQueryable<IotDevice>> DeviceQueryAsync(long? orgId = null)
    {
        var scope = await GetScopeAsync();
        var query = ApplyScope(_deviceRep.AsQueryable(), scope);

        if (orgId is > 0)
        {
            await EnsureOrgAccessAsync(orgId.Value, scope);
            var orgIds = await _sysOrgService.GetChildIdListWithSelfById(orgId.Value);
            query = query.Where(u => orgIds.Contains(u.OrgId));
        }
        return query;
    }

    /// <summary>
    /// 获取组织快照数据的访问范围；指定组织时包含其下级组织
    /// </summary>
    public async Task<IotOrgScope> GetOrgFilterAsync(long? orgId = null)
    {
        var scope = await GetScopeAsync();
        if (orgId is not > 0) return scope;

        await EnsureOrgAccessAsync(orgId.Value, scope);
        var orgIds = await _sysOrgService.GetChildIdListWithSelfById(orgId.Value);
        if (!scope.IsUnrestricted) orgIds = orgIds.Where(scope.OrgIds.Contains).ToList();
        return new IotOrgScope { OrgIds = orgIds.Distinct().ToList() };
    }

    /// <summary>
    /// 获取并校验一批设备，任一设备不存在或越权时整体拒绝
    /// </summary>
    public async Task<List<IotDevice>> GetAccessibleDevicesAsync(IEnumerable<long> deviceIds)
    {
        var ids = deviceIds?.Distinct().ToList() ?? new List<long>();
        if (ids.Count == 0) return new List<IotDevice>();

        var devices = await (await DeviceQueryAsync()).Where(u => ids.Contains(u.Id)).ToListAsync();
        if (devices.Count != ids.Count) throw Oops.Oh("部分设备不存在或无权访问");
        return devices;
    }

    /// <summary>
    /// 获取并校验单台设备
    /// </summary>
    public async Task<IotDevice> EnsureDeviceAccessAsync(long deviceId)
    {
        var device = await (await DeviceQueryAsync()).FirstAsync(u => u.Id == deviceId);
        return device ?? throw Oops.Oh("设备不存在或无权访问");
    }

    /// <summary>
    /// 按设备名称获取并校验单台设备
    /// </summary>
    public async Task<IotDevice> EnsureDeviceAccessAsync(string deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) throw Oops.Oh("设备名称不能为空");
        var device = await (await DeviceQueryAsync()).FirstAsync(u => u.DeviceName == deviceName);
        return device ?? throw Oops.Oh("设备不存在或无权访问");
    }

    /// <summary>
    /// 解析新增/修改设备的组织归属
    /// </summary>
    public async Task<long> ResolveOrgIdAsync(long requestedOrgId = 0)
    {
        var scope = await GetScopeAsync();
        var orgId = requestedOrgId > 0 ? requestedOrgId : _userManager.OrgId;
        if (orgId <= 0 && scope.IsUnrestricted) orgId = SqlSugarConst.DefaultTenantId;
        if (orgId <= 0 && scope.OrgIds.Count == 1) orgId = scope.OrgIds[0];
        if (orgId <= 0) throw Oops.Oh("请选择所属组织");

        await EnsureOrgAccessAsync(orgId, scope);
        return orgId;
    }

    /// <summary>
    /// 校验组织存在且当前用户可管理
    /// </summary>
    public async Task EnsureOrgAccessAsync(long orgId)
    {
        await EnsureOrgAccessAsync(orgId, await GetScopeAsync());
    }

    /// <summary>
    /// 完整覆盖恢复会影响全部组织，仅允许超级管理员执行
    /// </summary>
    public void EnsureSuperAdmin()
    {
        if (!_userManager.SuperAdmin) throw Oops.Oh("仅超级管理员可执行此操作");
    }

    private async Task EnsureOrgAccessAsync(long orgId, IotOrgScope scope)
    {
        if (orgId <= 0 || !await _orgRep.AsQueryable().AnyAsync(u => u.Id == orgId))
            throw Oops.Oh("所属组织不存在");
        if (!scope.IsUnrestricted && !scope.OrgIds.Contains(orgId))
            throw Oops.Oh("无权管理该组织的数据");
    }

    private static ISugarQueryable<IotDevice> ApplyScope(ISugarQueryable<IotDevice> query, IotOrgScope scope)
    {
        if (scope.IsUnrestricted) return query;
        if (scope.OrgIds.Count == 0) return query.Where(u => u.Id == 0);
        return query.Where(u => scope.OrgIds.Contains(u.OrgId));
    }
}
