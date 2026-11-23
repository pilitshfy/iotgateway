using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Admin.NET.Web.Core;

/// <summary>
/// 时序历史数据查询器：直接查 TimescaleDB（raw / 降采样聚合）
/// </summary>
public class IotHistoryQuery : IIotHistoryQuery
{
    private readonly IotHistoryOptions _options;
    private readonly ILogger<IotHistoryQuery> _logger;

    public IotHistoryQuery(IOptions<IotHistoryOptions> options, ILogger<IotHistoryQuery> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<List<IotHistoryPointDto>> QueryAsync(IotHistoryQueryInput input)
    {
        var result = new List<IotHistoryPointDto>();
        if (string.IsNullOrWhiteSpace(_options.ConnectionString) || input == null) return result;

        var variableIds = input.VariableIds?.Where(id => id > 0).Distinct().ToList() ?? new List<long>();
        if (variableIds.Count == 0) return result;

        // 时间统一按 UTC 区间处理（入参为 ISO 8601 字符串；带时区按其时区，无时区按 UTC）
        var end = ParseTime(input.EndTime) ?? DateTime.UtcNow;
        var start = ParseTime(input.StartTime) ?? end.AddHours(-1);
        if (start > end) (start, end) = (end, start);

        var isRaw = string.IsNullOrWhiteSpace(input.Aggregate) || input.Aggregate.Equals("raw", StringComparison.OrdinalIgnoreCase);
        var agg = isRaw ? "raw" : NormalizeAggregate(input.Aggregate);
        var bucket = input.BucketSeconds ?? 60;
        if (bucket < 1) bucket = 1;

        _logger.LogDebug("IotHistoryQuery: device={Device}, vars={@Vars}({Count}), startUtc={Start:o}, endUtc={End:o}, agg={Agg}, bucket={Bucket}",
            input.DeviceId, variableIds, variableIds.Count, start, end, agg, bucket);

        try
        {
            await using var conn = new NpgsqlConnection(_options.ConnectionString);
            await conn.OpenAsync();

            if (isRaw)
            {
                var limit = input.Limit ?? 10000;
                const string sql = @"
SELECT time, variable_id, value, status
FROM iot_metric
WHERE device_id = @device AND variable_id = ANY(@vars) AND time >= @start AND time <= @end
ORDER BY time ASC
LIMIT @limit";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@device", input.DeviceId);
                cmd.Parameters.Add(new NpgsqlParameter("@vars", NpgsqlDbType.Bigint | NpgsqlDbType.Array) { Value = variableIds.ToArray() });
                // 显式指定 timestamptz，避免 Npgsql 默认按 timestamp(无时区) 发送导致会话时区偏移
                cmd.Parameters.Add(new NpgsqlParameter("@start", NpgsqlDbType.TimestampTz) { Value = start });
                cmd.Parameters.Add(new NpgsqlParameter("@end", NpgsqlDbType.TimestampTz) { Value = end });
                cmd.Parameters.AddWithValue("@limit", limit);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new IotHistoryPointDto
                    {
                        Time = FormatIsoUtc(reader.GetDateTime(0)),
                        VariableId = reader.GetInt64(1),
                        Value = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        Status = reader.GetInt32(3)
                    });
                }
            }
            else
            {
                var aggExpr = agg == "count" ? "count(value)" : $"{agg}(value)";
                var sql = $@"
SELECT time_bucket(make_interval(secs => @bucket), time) AS bucket,
       variable_id,
       {aggExpr} AS value,
       0 AS status
FROM iot_metric
WHERE device_id = @device AND variable_id = ANY(@vars) AND time >= @start AND time <= @end
GROUP BY bucket, variable_id
ORDER BY bucket ASC";
                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@bucket", bucket);
                cmd.Parameters.AddWithValue("@device", input.DeviceId);
                cmd.Parameters.Add(new NpgsqlParameter("@vars", NpgsqlDbType.Bigint | NpgsqlDbType.Array) { Value = variableIds.ToArray() });
                cmd.Parameters.Add(new NpgsqlParameter("@start", NpgsqlDbType.TimestampTz) { Value = start });
                cmd.Parameters.Add(new NpgsqlParameter("@end", NpgsqlDbType.TimestampTz) { Value = end });

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(new IotHistoryPointDto
                    {
                        Time = FormatIsoUtc(reader.GetDateTime(0)),
                        VariableId = reader.GetInt64(1),
                        Value = reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        Status = 0
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IotHistory 查询失败：device={Device} vars={Count} agg={Agg}", input.DeviceId, variableIds.Count, agg);
        }

        _logger.LogDebug("IotHistoryQuery 返回 {Count} 行（device={Device} agg={Agg}）", result.Count, input.DeviceId, agg);
        return result;
    }

    private static string NormalizeAggregate(string agg)
    {
        var allowed = new[] { "avg", "max", "min", "sum", "count" };
        var a = agg?.Trim().ToLowerInvariant();
        return allowed.Contains(a) ? a : "avg";
    }

    /// <summary>
    /// 解析 ISO 8601 时间字符串为 UTC；无时区信息时按 UTC 处理
    /// </summary>
    private static DateTime? ParseTime(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
        }
        return null;
    }

    /// <summary>
    /// 格式化为 ISO 8601 UTC 字符串（带 Z），供前端跨时区正确解析
    /// </summary>
    private static string FormatIsoUtc(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Utc ? dt : dt.ToUniversalTime();
        return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
