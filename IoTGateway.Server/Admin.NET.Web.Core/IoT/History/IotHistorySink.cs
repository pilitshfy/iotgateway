using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Admin.NET.Web.Core;

/// <summary>
/// 时序历史数据写入器：内部有界队列批量落 TimescaleDB
/// </summary>
public class IotHistorySink : BackgroundService, IIotHistorySink
{
    private readonly IotHistoryOptions _options;
    private readonly ILogger<IotHistorySink> _logger;
    private readonly Channel<MetricPoint> _channel;
    private readonly string _connectionString;

    private int _droppedCount;

    public bool Enabled => _options.Enabled && !string.IsNullOrWhiteSpace(_connectionString);

    public IotHistorySink(IOptions<IotHistoryOptions> options, ILogger<IotHistorySink> logger)
    {
        _options = options.Value;
        _logger = logger;
        _connectionString = _options.ConnectionString ?? string.Empty;

        var capacity = Math.Max(_options.BatchSize * 8, 4096);
        _channel = Channel.CreateBounded<MetricPoint>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(long deviceId, long variableId, object value, int status, DateTime timestamp)
    {
        if (!Enabled) return;

        ConvertValue(value, out var numeric, out var text);
        var point = new MetricPoint
        {
            Time = timestamp.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc) : timestamp.ToUniversalTime(),
            DeviceId = deviceId,
            VariableId = variableId,
            Value = numeric,
            ValueText = text,
            Status = status
        };

        if (!_channel.Writer.TryWrite(point))
        {
            Interlocked.Increment(ref _droppedCount);
        }
    }

    private static void ConvertValue(object value, out double? numeric, out string text)
    {
        numeric = null;
        text = null;
        if (value == null) return;

        switch (value)
        {
            case bool b:
                numeric = b ? 1d : 0d;
                return;
            case sbyte sb: numeric = sb; return;
            case byte by: numeric = by; return;
            case short s: numeric = s; return;
            case ushort us: numeric = us; return;
            case int i: numeric = i; return;
            case uint ui: numeric = ui; return;
            case long l: numeric = l; return;
            case ulong ul: numeric = ul; return;
            case float f: numeric = f; return;
            case double d: numeric = d; return;
            case decimal dec: numeric = (double)dec; return;
            case string str:
                if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                    numeric = parsed;
                else
                    text = str;
                return;
            default:
                text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Enabled)
        {
            _logger.LogInformation("IotHistory 未启用或未配置连接串，时序落库跳过");
            return;
        }

        try
        {
            await EnsureSchemaAsync(stoppingToken);
            _logger.LogInformation("IotHistory 启动，连接 {Host}/{Db}，批量 {Batch}，间隔 {Ms}ms，保留 {Days} 天",
                GetHost(), GetDb(), _options.BatchSize, _options.FlushIntervalMs, _options.RetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IotHistory 初始化 TimescaleDB 失败，历史落库已禁用");
            return;
        }

        var batch = new List<MetricPoint>(_options.BatchSize);
        var flushInterval = Math.Max(_options.FlushIntervalMs, 100);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 凑满 BatchSize，或超过 FlushIntervalMs 即刷写
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                cts.CancelAfter(flushInterval);
                try
                {
                    while (batch.Count < _options.BatchSize)
                    {
                        var point = await _channel.Reader.ReadAsync(cts.Token);
                        batch.Add(point);
                    }
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                {
                    // 超时，刷写当前已入队批次
                }

                if (batch.Count > 0)
                {
                    await FlushAsync(batch, stoppingToken);
                    batch.Clear();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IotHistory 写入循环异常退出");
        }
        finally
        {
            // 退出前尽量刷完剩余
            while (_channel.Reader.TryRead(out var point)) batch.Add(point);
            if (batch.Count > 0)
            {
                try { await FlushAsync(batch, CancellationToken.None); } catch (Exception ex) { _logger.LogError(ex, "IotHistory 退出刷写失败"); }
            }
        }
    }

    private async Task FlushAsync(List<MetricPoint> batch, CancellationToken token)
    {
        if (batch.Count == 0) return;
        const int maxRetry = 3;
        for (var attempt = 0; attempt < maxRetry; attempt++)
        {
            try
            {
                await using var conn = new NpgsqlConnection(_connectionString);
                await conn.OpenAsync(token);
                await using var writer = conn.BeginBinaryImport(
                    "COPY iot_metric (time, device_id, variable_id, value, value_text, status) FROM STDIN (FORMAT BINARY)");
                foreach (var p in batch)
                {
                    writer.StartRow();
                    writer.Write(p.Time, NpgsqlDbType.TimestampTz);
                    writer.Write(p.DeviceId, NpgsqlDbType.Bigint);
                    writer.Write(p.VariableId, NpgsqlDbType.Bigint);
                    if (p.Value.HasValue) writer.Write(p.Value.Value, NpgsqlDbType.Double); else writer.WriteNull();
                    if (p.ValueText != null) writer.Write(p.ValueText, NpgsqlDbType.Text); else writer.WriteNull();
                    writer.Write(p.Status, NpgsqlDbType.Integer);
                }
                await writer.CompleteAsync(token);

                var dropped = Interlocked.Exchange(ref _droppedCount, 0);
                if (dropped > 0)
                    _logger.LogWarning("IotHistory 队列溢出丢弃 {Count} 条（写入速度跟不上采集）", dropped);
                return;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetry - 1)
                    _logger.LogError(ex, "IotHistory 批量写入 {Count} 条失败（已重试 {Retry} 次）", batch.Count, maxRetry);
                else
                    await Task.Delay(500 * (attempt + 1), token);
            }
        }
    }

    private async Task EnsureSchemaAsync(CancellationToken token)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(token);

        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS timescaledb;", conn))
            await cmd.ExecuteNonQueryAsync(token);

        await using (var cmd = new NpgsqlCommand(@"
CREATE TABLE IF NOT EXISTS iot_metric (
  time timestamptz NOT NULL,
  device_id bigint NOT NULL,
  variable_id bigint NOT NULL,
  value double precision,
  value_text text,
  status integer NOT NULL DEFAULT 0
);", conn))
            await cmd.ExecuteNonQueryAsync(token);

        await using (var cmd = new NpgsqlCommand(
            "SELECT create_hypertable('iot_metric','time', if_not_exists => TRUE, chunk_time_interval => INTERVAL '1 day');", conn))
            await cmd.ExecuteNonQueryAsync(token);

        await using (var cmd = new NpgsqlCommand(
            "CREATE INDEX IF NOT EXISTS idx_iot_metric_dv_time ON iot_metric (device_id, variable_id, time DESC);", conn))
            await cmd.ExecuteNonQueryAsync(token);

        try
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT remove_retention_policy('iot_metric', if_exists => TRUE);", conn);
            await cmd.ExecuteNonQueryAsync(token);
        }
        catch { /* 忽略 */ }

        if (_options.RetentionDays > 0)
        {
            await using var cmd = new NpgsqlCommand(
                $"SELECT add_retention_policy('iot_metric', make_interval(days => {_options.RetentionDays}));", conn);
            await cmd.ExecuteNonQueryAsync(token);
        }
    }

    private string GetHost()
    {
        try { return new NpgsqlConnectionStringBuilder(_connectionString).Host; } catch { return "?"; }
    }

    private string GetDb()
    {
        try { return new NpgsqlConnectionStringBuilder(_connectionString).Database; } catch { return "?"; }
    }

    private struct MetricPoint
    {
        public DateTime Time;
        public long DeviceId;
        public long VariableId;
        public double? Value;
        public string ValueText;
        public int Status;
    }
}
