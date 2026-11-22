using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
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

    public IotHistoryService(IServiceProvider serviceProvider, ILogger<IotHistoryService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    private IIotHistoryQuery HistoryQuery => _serviceProvider.GetService<IIotHistoryQuery>();

    /// <summary>
    /// 查询变量历史数据（raw 或降采样聚合）
    /// </summary>
    [DisplayName("历史数据查询"), HttpPost("queryHistory")]
    public async Task<List<IotHistoryPointDto>> QueryHistory([FromBody] IotHistoryQueryInput input)
    {
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
