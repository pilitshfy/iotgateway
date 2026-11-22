using System.ComponentModel;
using System.Data;
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
    /// RPC 日志详情
    /// </summary>
    [DisplayName("RPC日志详情"), HttpGet]
    public async Task<IotRpcLog> Detail([FromQuery] long id)
    {
        return await _rpcLogRep.GetByIdAsync(id);
    }

    /// <summary>
    /// 导出 RPC 日志 Excel
    /// </summary>
    [DisplayName("导出RPC日志"), ApiDescriptionSettings(Name = "Export"), HttpGet]
    public async Task<IActionResult> Export([FromQuery] long? deviceId, [FromQuery] string keyword)
    {
        var logs = await _rpcLogRep.AsQueryable()
            .WhereIF(deviceId.HasValue, u => u.DeviceId == deviceId)
            .WhereIF(!string.IsNullOrWhiteSpace(keyword), u => u.Method.Contains(keyword))
            .OrderBy(u => u.StartTime, OrderByType.Desc)
            .ToListAsync();
        var devices = await _rpcLogRep.Context.Queryable<IotDevice>().ToListAsync();
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
