using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 边缘节点入站服务：订阅内嵌 MQTT broker，处理心跳与遥测上报。
/// 主题约定：
///   edge/{nodeCode}/heartbeat  -> 在线心跳（payload 可选 {"version":"x.y.z"}）
///   edge/{nodeCode}/telemetry  -> 遥测上报（payload 单条对象或对象数组）
/// 遥测字段：deviceId/variableId（或 deviceName/variableName）、value、status(可选)、ts(可选)
/// </summary>
public class IotEdgeIngestService : BackgroundService
{
    private readonly IotMqttServer _mqtt;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IotEdgeIngestService> _logger;
    private readonly TaskCompletionSource _stopped = new();

    public IotEdgeIngestService(IotMqttServer mqtt, IServiceScopeFactory scopeFactory, ILogger<IotEdgeIngestService> logger)
    {
        _mqtt = mqtt;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _mqtt.MessageReceived += OnMessageAsync;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => _stopped.Task.WaitAsync(stoppingToken);

    public override void Dispose()
    {
        _mqtt.MessageReceived -= OnMessageAsync;
        _stopped.TrySetResult();
        base.Dispose();
    }

    private async Task OnMessageAsync(string topic, string payload)
    {
        try
        {
            if (string.IsNullOrEmpty(topic) || !topic.StartsWith("edge/", StringComparison.OrdinalIgnoreCase)) return;
            var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return; // edge/{nodeCode}/{kind}
            var nodeCode = parts[1];
            var kind = parts[2];
            using var scope = _scopeFactory.CreateScope();
            if (kind.Equals("heartbeat", StringComparison.OrdinalIgnoreCase))
                await HandleHeartbeatAsync(scope, nodeCode, payload);
            else if (kind.Equals("telemetry", StringComparison.OrdinalIgnoreCase))
                await HandleTelemetryAsync(scope, nodeCode, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理边缘节点入站消息失败：{Topic}", topic);
        }
    }

    private static ISqlSugarClient Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().CopyNew();

    private async Task HandleHeartbeatAsync(IServiceScope scope, string nodeCode, string payload)
    {
        var db = Db(scope);
        var node = await db.Queryable<IotEdgeNode>().FirstAsync(u => u.NodeCode == nodeCode);
        if (node == null || !node.Enabled) return;

        var version = node.Version;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("version", out var v)
                    && v.ValueKind == JsonValueKind.String)
                    version = v.GetString();
            }
            catch { }
        }
        var newVersion = version ?? node.Version;
        var now = DateTime.UtcNow;
        await db.Updateable<IotEdgeNode>()
            .SetColumns(u => u.Online == true)
            .SetColumns(u => u.LastHeartbeatTime == now)
            .SetColumns(u => u.Version == newVersion)
            .Where(u => u.Id == node.Id)
            .ExecuteCommandAsync();
    }

    private async Task HandleTelemetryAsync(IServiceScope scope, string nodeCode, string payload)
    {
        var db = Db(scope);
        var node = await db.Queryable<IotEdgeNode>().FirstAsync(u => u.NodeCode == nodeCode);
        if (node == null) { _logger.LogWarning("遥测上报：节点编号不存在 {NodeCode}", nodeCode); return; }
        if (!node.Enabled) return;

        var items = ParseTelemetry(payload);
        if (items.Count == 0) { _logger.LogWarning("遥测上报：无可解析测点 node={NodeCode}", nodeCode); return; }

        // 解析 deviceId/variableId（支持按名称回退解析）
        foreach (var it in items)
        {
            if (it.DeviceId <= 0 && !string.IsNullOrWhiteSpace(it.DeviceName))
            {
                var dev = await db.Queryable<IotDevice>().FirstAsync(u => u.DeviceName == it.DeviceName);
                it.DeviceId = dev?.Id ?? 0;
            }
            if (it.VariableId <= 0 && it.DeviceId > 0 && !string.IsNullOrWhiteSpace(it.VariableName))
            {
                var v = await db.Queryable<IotDeviceVariable>().FirstAsync(u => u.DeviceId == it.DeviceId && u.Name == it.VariableName);
                it.VariableId = v?.Id ?? 0;
            }
        }
        var valid = items.Where(i => i.DeviceId > 0 && i.VariableId > 0).ToList();
        if (valid.Count == 0) { _logger.LogWarning("遥测上报：未解析到有效测点 node={NodeCode}", nodeCode); return; }

        var devIds = valid.Select(i => i.DeviceId).Distinct().ToList();
        var devices = await db.Queryable<IotDevice>().In(u => u.Id, devIds).ToListAsync();
        var devMap = devices.ToDictionary(d => d.Id);
        var varIds = valid.Select(i => i.VariableId).Distinct().ToList();
        var variables = await db.Queryable<IotDeviceVariable>().In(u => u.Id, varIds).ToListAsync();
        var varMap = variables.ToDictionary(v => v.Id);

        var history = scope.ServiceProvider.GetRequiredService<IIotHistorySink>();
        var alarm = scope.ServiceProvider.GetRequiredService<IIotAlarmSink>();
        var now = DateTime.Now;

        foreach (var it in valid)
        {
            var ts = it.Timestamp ?? now;
            history.Enqueue(it.DeviceId, it.VariableId, it.Value, (int)it.Status, ts);
            if (devMap.TryGetValue(it.DeviceId, out var d)
                && varMap.TryGetValue(it.VariableId, out var v)
                && v.DeviceId == it.DeviceId)
            {
                alarm.Enqueue(new IotAlarmSample
                {
                    DeviceId = d.Id,
                    DeviceName = d.DeviceName,
                    OrgId = d.OrgId,
                    VariableId = v.Id,
                    VariableName = v.Name,
                    Value = it.Value,
                    Status = it.Status,
                    Timestamp = ts,
                });
            }
        }

        var utcNow = DateTime.UtcNow;
        await db.Updateable<IotEdgeNode>()
            .SetColumns(u => u.Online == true)
            .SetColumns(u => u.LastReportTime == utcNow)
            .SetColumns(u => u.LastHeartbeatTime == utcNow)
            .Where(u => u.Id == node.Id)
            .ExecuteCommandAsync();
    }

    private List<TelemetryItem> ParseTelemetry(string payload)
    {
        var list = new List<TelemetryItem>();
        if (string.IsNullOrWhiteSpace(payload)) return list;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                    list.Add(ReadItem(el));
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                list.Add(ReadItem(doc.RootElement));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "遥测 payload 解析失败：{Payload}", payload);
        }
        return list;
    }

    private static TelemetryItem ReadItem(JsonElement el)
    {
        var item = new TelemetryItem();
        if (el.TryGetProperty("deviceId", out var d) && d.ValueKind == JsonValueKind.Number) item.DeviceId = d.GetInt64();
        if (el.TryGetProperty("variableId", out var v) && v.ValueKind == JsonValueKind.Number) item.VariableId = v.GetInt64();
        if (el.TryGetProperty("deviceName", out var dn) && dn.ValueKind == JsonValueKind.String) item.DeviceName = dn.GetString();
        if (el.TryGetProperty("variableName", out var vn) && vn.ValueKind == JsonValueKind.String) item.VariableName = vn.GetString();
        if (el.TryGetProperty("value", out var val)) item.Value = ToClrValue(val);
        if (el.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number) item.Status = (IotVariableStatusEnum)st.GetInt32();
        if (el.TryGetProperty("ts", out var ts) && ts.ValueKind == JsonValueKind.String
            && DateTime.TryParse(ts.GetString(), out var dt))
            item.Timestamp = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Local) : dt;
        return item;
    }

    private static object ToClrValue(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetInt64(out var l) ? l : el.GetDouble();
            case JsonValueKind.String:
                return el.GetString();
            case JsonValueKind.True:
            case JsonValueKind.False:
                return el.GetBoolean();
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            default:
                return el.GetRawText();
        }
    }

    private sealed class TelemetryItem
    {
        public long DeviceId { get; set; }
        public long VariableId { get; set; }
        public string DeviceName { get; set; }
        public string VariableName { get; set; }
        public object Value { get; set; }
        public IotVariableStatusEnum Status { get; set; }
        public DateTime? Timestamp { get; set; }
    }
}
