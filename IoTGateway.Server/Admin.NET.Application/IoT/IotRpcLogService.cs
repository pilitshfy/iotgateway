using System.ComponentModel;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT RPC 日志服务
/// </summary>
[ApiDescriptionSettings(Order = 455)]
public class IotRpcLogService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotRpcLog> _rpcLogRep;

    public IotRpcLogService(SqlSugarRepository<IotRpcLog> rpcLogRep)
    {
        _rpcLogRep = rpcLogRep;
    }

    /// <summary>
    /// RPC 日志分页列表
    /// </summary>
    [DisplayName("RPC日志分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotRpcLog>> Page([FromBody] IotRpcLogPageInput input)
    {
        return await _rpcLogRep.AsQueryable()
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.Method.Contains(input.Keyword))
            .OrderBy(u => u.StartTime, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    /// <summary>
    /// 删除 RPC 日志
    /// </summary>
    [DisplayName("删除RPC日志"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        await _rpcLogRep.DeleteByIdAsync(input.Id);
    }

    /// <summary>
    /// 批量删除 RPC 日志
    /// </summary>
    [DisplayName("批量删除RPC日志"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;
        await _rpcLogRep.Context.Deleteable<IotRpcLog>().In(input.Ids).ExecuteCommandAsync();
    }

    /// <summary>
    /// 清空 RPC 日志
    /// </summary>
    [DisplayName("清空RPC日志"), ApiDescriptionSettings(Name = "Clear"), HttpPost]
    public async Task Clear()
    {
        await _rpcLogRep.Context.Deleteable<IotRpcLog>().Where(u => u.Id > 0).ExecuteCommandAsync();
    }
}
