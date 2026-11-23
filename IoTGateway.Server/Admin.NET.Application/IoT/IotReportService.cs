using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Furion.FriendlyException;
using Microsoft.AspNetCore.Mvc;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 报表 BI：按变量 + 聚合方式 + 时间粒度生成表格数据（数据源为 TimescaleDB 时序库）
/// </summary>
[ApiDescriptionSettings(Order = 458)]
public class IotReportService : IDynamicApiController, ITransient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IotOrgScopeService _orgScope;
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;

    public IotReportService(
        IServiceProvider serviceProvider,
        IotOrgScopeService orgScope,
        SqlSugarRepository<IotDeviceVariable> variableRep)
    {
        _serviceProvider = serviceProvider;
        _orgScope = orgScope;
        _variableRep = variableRep;
    }

    private IIotHistoryQuery HistoryQuery => _serviceProvider.GetService<IIotHistoryQuery>();

    /// <summary>
    /// 报表查询：选变量 + 聚合方式 + 时间粒度 → 时间桶表格（行=时间桶，列=变量）
    /// </summary>
    [DisplayName("报表查询"), HttpPost("query")]
    public async Task<IotReportResultDto> Query([FromBody] IotReportQueryInput input)
    {
        if (input == null) throw Oops.Oh("查询参数不能为空");
        if (input.DeviceId <= 0) throw Oops.Oh("请选择设备");

        var device = await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
        var agg = NormalizeAggregate(input.Aggregate);
        var (granularity, bucketSeconds) = ResolveGranularity(input.Granularity);

        var varQuery = _variableRep.AsQueryable().Where(u => u.DeviceId == input.DeviceId);
        if (input.VariableIds != null && input.VariableIds.Count > 0)
        {
            var ids = input.VariableIds.Where(u => u > 0).Distinct().ToList();
            if (ids.Count == 0) throw Oops.Oh("变量参数无效");
            varQuery = varQuery.Where(u => ids.Contains(u.Id));
        }
        var variables = await varQuery.OrderBy(u => u.Id).ToListAsync();
        if (variables.Count == 0) throw Oops.Oh("该设备无可用于报表的变量");

        var result = new IotReportResultDto
        {
            DeviceId = device.Id,
            DeviceName = device.DeviceName,
            Aggregate = agg,
            Granularity = granularity,
            BucketSeconds = bucketSeconds,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            Columns = variables.Select(v => new IotReportColumnDto
            {
                VariableId = v.Id,
                Name = v.Name,
                Description = v.Description
            }).ToList()
        };

        var q = HistoryQuery;
        if (q == null) return result;

        var points = await q.QueryAsync(new IotHistoryQueryInput
        {
            DeviceId = input.DeviceId,
            VariableIds = variables.Select(v => v.Id).ToList(),
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            Aggregate = agg,
            BucketSeconds = agg == "raw" ? null : (int?)bucketSeconds,
            Limit = agg == "raw" ? 20000 : (int?)null
        });

        var varSet = variables.Select(v => v.Id).ToHashSet();
        var rows = new SortedDictionary<string, IotReportRowDto>(StringComparer.Ordinal);
        foreach (var p in points)
        {
            if (!varSet.Contains(p.VariableId)) continue;
            if (!rows.TryGetValue(p.Time, out var row))
            {
                row = new IotReportRowDto { Time = p.Time };
                foreach (var v in variables) row.Values[v.Id] = null;
                rows[p.Time] = row;
            }
            row.Values[p.VariableId] = p.Value;
        }
        result.Rows = rows.Values.ToList();
        return result;
    }

    private static string NormalizeAggregate(string agg)
    {
        var a = string.IsNullOrWhiteSpace(agg) ? "avg" : agg.Trim().ToLowerInvariant();
        var allowed = new[] { "raw", "avg", "max", "min", "sum", "count" };
        if (!allowed.Contains(a)) throw Oops.Oh("不支持的聚合方式");
        return a;
    }

    private static (string, int) ResolveGranularity(string g)
    {
        var s = string.IsNullOrWhiteSpace(g) ? "hour" : g.Trim().ToLowerInvariant();
        switch (s)
        {
            case "minute": return ("minute", 60);
            case "hour": return ("hour", 3600);
            case "day": return ("day", 86400);
            case "month": return ("month", 2592000);
            default: throw Oops.Oh("不支持的时间粒度");
        }
    }
}
