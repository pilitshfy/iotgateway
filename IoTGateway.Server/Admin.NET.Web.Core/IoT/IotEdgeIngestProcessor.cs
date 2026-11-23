using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 边缘节点入站统一处理器：被 MQTT 消费者与 Kafka 消费者共用。
/// kind：heartbeat / telemetry。
/// 遥测字段：deviceId/variableId（或 deviceName/variableName）、value、status(可选)、ts(可选)。
/// </summary>
public class IotEdgeIngestProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IotEdgeIngestProcessor> _logger;

    public IotEdgeIngestProcessor(IServiceScopeFactory scopeFactory, ILogger<IotEdgeIngestProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>统一入口：按 kind 分发到心跳或遥测</summary>
    public async Task HandleAsync(string nodeCode, string kind, string payload)
    {
        if (string.IsNullOrWhiteSpace(nodeCode)) return;
        if (string.Equals(kind, "heartbeat", StringComparison.OrdinalIgnoreCase))
            await HandleHeartbeatAsync(nodeCode, payload);
        else if (string.Equals(kind, "telemetry", StringComparison.OrdinalIgnoreCase))
            await HandleTelemetryAsync(nodeCode, payload);
    }

    public async Task HandleHeartbeatAsync(string nodeCode, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = Db(scope);
        var node = await db.Queryable<IotEdgeNode>().FirstAsync(u => u.NodeCode == nodeCode);
        if (node == null || !node.Enabled) return;

        var version = node.Version;
        string accessKey = null;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(payload);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    accessKey = ReadString(doc.RootElement, "accessKey");
                    var v = ReadString(doc.RootElement, "version");
                    if (!string.IsNullOrWhiteSpace(v)) version = v;
                }
            }
            catch { }
        }
        if (!CheckAccessKey(node, accessKey, nodeCode)) return;
        var newVersion = version ?? node.Version;
        var now = DateTime.UtcNow;
        await db.Updateable<IotEdgeNode>()
            .SetColumns(u => u.Online == true)
            .SetColumns(u => u.LastHeartbeatTime == now)
            .SetColumns(u => u.Version == newVersion)
            .Where(u => u.Id == node.Id)
            .ExecuteCommandAsync();
    }

    public async Task HandleTelemetryAsync(string nodeCode, string payload)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = Db(scope);
        var node = await db.Queryable<IotEdgeNode>().FirstAsync(u => u.NodeCode == nodeCode);
        if (node == null) { _logger.LogWarning("遥测上报：节点编号不存在 {NodeCode}", nodeCode); return; }
        if (!node.Enabled) return;

        if (!CheckAccessKey(node, ExtractAccessKey(payload), nodeCode)) return;

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

    private static ISqlSugarClient Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISqlSugarClient>().CopyNew();

    private static string ReadString(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object
            && el.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String)
            return v.GetString();
        return null;
    }

    private static string ExtractAccessKey(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object) return ReadString(root, "accessKey");
            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                {
                    var k = ReadString(el, "accessKey");
                    if (!string.IsNullOrEmpty(k)) return k;
                }
            }
        }
        catch { }
        return null;
    }

    private bool CheckAccessKey(IotEdgeNode node, string provided, string nodeCode)
    {
        if (string.IsNullOrWhiteSpace(node.AccessKey))
        {
            _logger.LogWarning("边缘节点未配置接入密钥，拒绝 {NodeCode}", nodeCode);
            return false;
        }
        if (string.IsNullOrEmpty(provided) || !FixedTimeEquals(node.AccessKey, provided))
        {
            _logger.LogWarning("边缘节点接入密钥错误，拒绝 {NodeCode}", nodeCode);
            return false;
        }
        return true;
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
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
