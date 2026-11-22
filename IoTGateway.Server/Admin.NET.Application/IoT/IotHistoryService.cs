using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Furion.FriendlyException;
using SqlSugar;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Admin.NET.Application;

/// <summary>
/// IoT 时序历史数据查询接口（数据由 Web.Core 落 TimescaleDB）
/// </summary>
[ApiDescriptionSettings(Order = 457)]
public class IotHistoryService : IDynamicApiController, ITransient
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IotHistoryService> _logger;
    private readonly IotOrgScopeService _orgScope;
    private readonly SqlSugarRepository<IotDeviceVariable> _variableRep;

    public IotHistoryService(
        IServiceProvider serviceProvider,
        ILogger<IotHistoryService> logger,
        IotOrgScopeService orgScope,
        SqlSugarRepository<IotDeviceVariable> variableRep)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _orgScope = orgScope;
        _variableRep = variableRep;
    }

    private IIotHistoryQuery HistoryQuery => _serviceProvider.GetService<IIotHistoryQuery>();

    /// <summary>
    /// 查询变量历史数据（raw 或降采样聚合）
    /// </summary>
    [DisplayName("历史数据查询"), HttpPost("queryHistory")]
    public async Task<List<IotHistoryPointDto>> QueryHistory([FromBody] IotHistoryQueryInput input)
    {
        if (input == null) throw Oops.Oh("查询参数不能为空");
        await _orgScope.EnsureDeviceAccessAsync(input.DeviceId);
        var variableIds = input.VariableIds?.Where(u => u > 0).Distinct().ToList() ?? new List<long>();
        if (variableIds.Count > 0)
        {
            var ownedCount = await _variableRep.AsQueryable()
                .Where(u => u.DeviceId == input.DeviceId && variableIds.Contains(u.Id))
                .CountAsync();
            if (ownedCount != variableIds.Count) throw Oops.Oh("部分变量不存在、不属于该设备或无权访问");
        }

        var q = HistoryQuery;
        if (q == null)
        {
            _logger.LogWarning("IotHistoryService: IIotHistoryQuery 未解析到，返回空");
            return new List<IotHistoryPointDto>();
        }
        _logger.LogDebug("IotHistoryService 查询 device={Device} vars={@Vars} agg={Agg}", input?.DeviceId, input?.VariableIds, input?.Aggregate);
        return await q.QueryAsync(input);
    }
}
