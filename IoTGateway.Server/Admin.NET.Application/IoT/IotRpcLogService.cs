using System.ComponentModel;
using System.Data;
using Furion.FriendlyException;
using Microsoft.AspNetCore.Mvc;
using MiniExcelLibs;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT RPC 日志服务
/// </summary>
[ApiDescriptionSettings(Order = 455)]
public class IotRpcLogService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotRpcLog> _rpcLogRep;
    private readonly IotOrgScopeService _orgScope;

    public IotRpcLogService(SqlSugarRepository<IotRpcLog> rpcLogRep, IotOrgScopeService orgScope)
    {
        _rpcLogRep = rpcLogRep;
        _orgScope = orgScope;
    }

    /// <summary>
    /// RPC 日志分页列表
    /// </summary>
    [DisplayName("RPC日志分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotRpcLog>> Page([FromBody] IotRpcLogPageInput input)
    {
        if (input.DeviceId.HasValue) await _orgScope.EnsureDeviceAccessAsync(input.DeviceId.Value);
        var query = await ApplyScopeAsync();
        return await query
            .WhereIF(input.DeviceId.HasValue, u => u.DeviceId == input.DeviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.Method.Contains(input.Keyword))
            .OrderBy(u => u.StartTime, OrderByType.Desc)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    /// <summary>
    /// RPC 日志详情
    /// </summary>
    [DisplayName("RPC日志详情"), HttpGet]
    public async Task<IotRpcLog> Detail([FromQuery] long id)
    {
        var log = await _rpcLogRep.GetByIdAsync(id) ?? throw Oops.Oh("RPC日志不存在");
        await EnsureLogAccessAsync(log);
        return log;
    }

    /// <summary>
    /// 导出 RPC 日志 Excel
    /// </summary>
    [DisplayName("导出RPC日志"), ApiDescriptionSettings(Name = "Export"), HttpGet]
    public async Task<IActionResult> Export([FromQuery] long? deviceId, [FromQuery] string keyword)
    {
        if (deviceId.HasValue) await _orgScope.EnsureDeviceAccessAsync(deviceId.Value);
        var logs = await (await ApplyScopeAsync())
            .WhereIF(deviceId.HasValue, u => u.DeviceId == deviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), u => u.Method.Contains(keyword))
            .OrderBy(u => u.StartTime, OrderByType.Desc)
            .ToListAsync();
        var devices = await (await _orgScope.DeviceQueryAsync()).ToListAsync();
        var deviceNames = devices.ToDictionary(u => u.Id, u => u.DeviceName);

        var table = new DataTable("RPC日志");
        foreach (var column in new[] { "发起方", "设备名", "方法", "请求参数", "开始时间", "结束时间", "结果", "描述" })
            table.Columns.Add(column);
        foreach (var log in logs)
        {
            table.Rows.Add(log.RpcSide == IotRpcSideEnum.ClientSide ? "客户端" : "服务端",
                log.DeviceId.HasValue ? deviceNames.GetValueOrDefault(log.DeviceId.Value) : null,
                log.Method, log.Params, log.StartTime, log.EndTime, log.IsSuccess ? "成功" : "失败", log.Description);
        }

        await using var stream = new MemoryStream();
        await stream.SaveAsAsync(table, printHeader: true);
        return new FileContentResult(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = $"iotgateway_rpclog_{DateTime.Now:yyyyMMddHHmmss}.xlsx"
        };
    }

    /// <summary>
    /// 删除 RPC 日志
    /// </summary>
    [DisplayName("删除RPC日志"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        var log = await _rpcLogRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("RPC日志不存在");
        await EnsureLogAccessAsync(log);
        await _rpcLogRep.DeleteByIdAsync(input.Id);
    }

    /// <summary>
    /// 批量删除 RPC 日志
    /// </summary>
    [DisplayName("批量删除RPC日志"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;
        var ids = input.Ids.Distinct().ToList();
        var logs = await _rpcLogRep.AsQueryable().Where(u => ids.Contains(u.Id)).ToListAsync();
        if (logs.Count != ids.Count) throw Oops.Oh("部分RPC日志不存在或无权访问");
        foreach (var log in logs) await EnsureLogAccessAsync(log);
        await _rpcLogRep.Context.Deleteable<IotRpcLog>().In(ids).ExecuteCommandAsync();
    }

    /// <summary>
    /// 清空 RPC 日志
    /// </summary>
    [DisplayName("清空RPC日志"), ApiDescriptionSettings(Name = "Clear"), HttpPost]
    public async Task Clear()
    {
        var scope = await _orgScope.GetScopeAsync();
        if (scope.IsUnrestricted)
        {
            await _rpcLogRep.Context.Deleteable<IotRpcLog>().Where(u => u.Id > 0).ExecuteCommandAsync();
            return;
        }

        var deviceIds = await (await _orgScope.DeviceQueryAsync()).Select(u => u.Id).ToListAsync();
        await _rpcLogRep.Context.Deleteable<IotRpcLog>()
            .Where(u => u.DeviceId.HasValue && deviceIds.Contains(u.DeviceId.Value))
            .ExecuteCommandAsync();
    }

    private async Task<ISugarQueryable<IotRpcLog>> ApplyScopeAsync()
    {
        var scope = await _orgScope.GetScopeAsync();
        var query = _rpcLogRep.AsQueryable();
        if (scope.IsUnrestricted) return query;

        var deviceIds = await (await _orgScope.DeviceQueryAsync()).Select(u => u.Id).ToListAsync();
        return query.Where(u => u.DeviceId.HasValue && deviceIds.Contains(u.DeviceId.Value));
    }

    private async Task EnsureLogAccessAsync(IotRpcLog log)
    {
        var scope = await _orgScope.GetScopeAsync();
        if (scope.IsUnrestricted) return;
        if (!log.DeviceId.HasValue) throw Oops.Oh("无权访问该RPC日志");
        await _orgScope.EnsureDeviceAccessAsync(log.DeviceId.Value);
    }
}
