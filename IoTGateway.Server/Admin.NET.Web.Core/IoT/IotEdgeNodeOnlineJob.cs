using System;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using Furion.Schedule;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 边缘节点离线检测作业：每 30s 扫描，超过 90s 未收到心跳的启用节点置为离线
/// </summary>
[JobDetail("job_iot_edge_online", Description = "边缘节点离线检测", GroupName = "default", Concurrent = false)]
[PeriodSeconds(30, TriggerId = "trigger_iot_edge_online", Description = "边缘节点离线检测", RunOnStart = true)]
public class IotEdgeNodeOnlineJob : IJob
{
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(90);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IotEdgeNodeOnlineJob> _logger;

    public IotEdgeNodeOnlineJob(IServiceScopeFactory scopeFactory, ILogger<IotEdgeNodeOnlineJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task ExecuteAsync(JobExecutingContext context, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().CopyNew();
        var cutoff = DateTime.UtcNow - StaleThreshold;
        var count = await db.Updateable<IotEdgeNode>()
            .SetColumns(u => u.Online == false)
            .Where(u => u.Enabled && u.Online == true && (u.LastHeartbeatTime == null || u.LastHeartbeatTime < cutoff))
            .ExecuteCommandAsync(stoppingToken);
        if (count > 0)
            _logger.LogInformation("边缘节点离线检测：{Count} 个节点因心跳超时(>90s)被置为离线", count);
    }
}
