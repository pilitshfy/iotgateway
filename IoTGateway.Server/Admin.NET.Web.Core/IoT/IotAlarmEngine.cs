using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Admin.NET.Application;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SqlSugar;

namespace Admin.NET.Web.Core;

/// <summary>
/// 实时告警引擎：单线程计算规则，异步发送通知，活动事件全生命周期持久化
/// </summary>
public class IotAlarmEngine : BackgroundService, IIotAlarmSink, IIotAlarmRuntime
{
    private readonly ISqlSugarClient _db;
    private readonly IIotAlarmNotifier _notifier;
    private readonly ILogger<IotAlarmEngine> _logger;
    private readonly Channel<IotAlarmSample> _samples;
    private readonly Channel<NotificationWork> _notifications;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Dictionary<long, List<RuleState>> _rulesByDevice = new();
    private readonly ConcurrentDictionary<long, SamplePair> _latestSamples = new();
    private int _droppedSamples;

    public bool Enabled => true;

    public IotAlarmEngine(ISqlSugarClient db, IIotAlarmNotifier notifier, ILogger<IotAlarmEngine> logger)
    {
        _db = db;
        _notifier = notifier;
        _logger = logger;
        _samples = Channel.CreateBounded<IotAlarmSample>(new BoundedChannelOptions(20000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _notifications = Channel.CreateBounded<NotificationWork>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void Enqueue(IotAlarmSample sample)
    {
        if (sample == null || sample.DeviceId <= 0 || sample.VariableId <= 0) return;
        if (!_samples.Writer.TryWrite(sample)) Interlocked.Increment(ref _droppedSamples);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(1500, stoppingToken);
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "告警引擎初始化失败");
        }

        var calculation = CalculateLoopAsync(stoppingToken);
        var notification = NotificationLoopAsync(stoppingToken);
        await Task.WhenAll(calculation, notification);
    }

    public async Task ReloadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var rules = await _db.Queryable<IotAlarmRule>().Where(u => u.Enabled).ToListAsync();
            var ruleIds = rules.Select(u => u.Id).ToList();
            var conditions = ruleIds.Count == 0
                ? new List<IotAlarmCondition>()
                : await _db.Queryable<IotAlarmCondition>().Where(u => ruleIds.Contains(u.RuleId)).OrderBy(u => u.Sort).ToListAsync();
            var activeEvents = await _db.Queryable<IotAlarmEvent>()
                .Where(u => u.Status == IotAlarmEventStatusEnum.Active || u.Status == IotAlarmEventStatusEnum.Acknowledged)
                .ToListAsync();

            var previousStates = _rulesByDevice.Values.SelectMany(u => u).ToDictionary(u => u.Rule.Id, u => u);
            var next = new Dictionary<long, List<RuleState>>();
            var attachedEventIds = new HashSet<long>();
            foreach (var rule in rules)
            {
                rule.Conditions = conditions.Where(u => u.RuleId == rule.Id).ToList();
                var state = new RuleState { Rule = rule };
                if (previousStates.TryGetValue(rule.Id, out var previous))
                {
                    state.TriggerStreak = previous.TriggerStreak;
                    state.RecoveryStreak = previous.RecoveryStreak;
                }

                var alarmEvent = activeEvents.FirstOrDefault(u => u.RuleId == rule.Id && u.DeviceId == rule.DeviceId);
                if (alarmEvent != null)
                {
                    state.ActiveEvent = alarmEvent;
                    attachedEventIds.Add(alarmEvent.Id);
                }

                if (!next.TryGetValue(rule.DeviceId, out var states)) next[rule.DeviceId] = states = new List<RuleState>();
                states.Add(state);
            }

            _rulesByDevice = next;

            // 已停用、已删除或改绑设备的规则，其遗留活动事件自动恢复，避免永久悬挂。
            foreach (var orphan in activeEvents.Where(u => !attachedEventIds.Contains(u.Id)))
            {
                await RecoverEventAsync(orphan, DateTime.Now, null, false);
            }

            _logger.LogInformation("告警规则已加载：{RuleCount} 条，活动事件 {EventCount} 个", rules.Count, attachedEventIds.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CalculateLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var sample in _samples.Reader.ReadAllAsync(token))
            {
                await _gate.WaitAsync(token);
                try
                {
                    await ProcessSampleAsync(sample, token);
                    var dropped = Interlocked.Exchange(ref _droppedSamples, 0);
                    if (dropped > 0) _logger.LogWarning("告警样本队列溢出，已丢弃 {Count} 条最旧样本", dropped);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理告警样本失败：{Device}/{Variable}", sample.DeviceName, sample.VariableName);
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }

    private async Task ProcessSampleAsync(IotAlarmSample sample, CancellationToken token)
    {
        _latestSamples.AddOrUpdate(sample.VariableId,
            _ => new SamplePair { Current = sample },
            (_, pair) => new SamplePair { Previous = pair.Current, Current = sample });

        if (!_rulesByDevice.TryGetValue(sample.DeviceId, out var states)) return;
        foreach (var state in states)
        {
            // 只有规则关联变量产生新样本时才累计连续触发/恢复次数，避免同设备其他变量误推进防抖。
            if (!state.Rule.Conditions.Any(condition => condition.VariableId == sample.VariableId)) continue;

            var conditionResults = state.Rule.Conditions.Select(condition => EvaluateCondition(condition, state.Rule.DeviceId)).ToList();
            var isTriggered = state.Rule.Conditions.Count > 0 && (state.Rule.CombineMode == IotAlarmCombineModeEnum.All
                ? conditionResults.All(u => u)
                : conditionResults.Any(u => u));

            if (isTriggered)
            {
                state.RecoveryStreak = 0;
                state.TriggerStreak++;
                if (state.ActiveEvent == null && state.TriggerStreak >= Math.Max(state.Rule.TriggerSamples, 1))
                    await CreateEventAsync(state, sample.Timestamp, token);
                else if (state.ActiveEvent != null)
                    await UpdateActiveEventAsync(state, sample.Timestamp, token);
            }
            else
            {
                state.TriggerStreak = 0;
                if (state.ActiveEvent == null) continue;

                state.RecoveryStreak++;
                if (state.RecoveryStreak >= Math.Max(state.Rule.RecoverySamples, 1))
                {
                    var alarmEvent = state.ActiveEvent;
                    state.ActiveEvent = null;
                    state.RecoveryStreak = 0;
                    await RecoverEventAsync(alarmEvent, sample.Timestamp, state.Rule.ChannelIds, true, token);
                }
            }
        }
    }

    private bool EvaluateCondition(IotAlarmCondition condition, long deviceId)
    {
        if (!_latestSamples.TryGetValue(condition.VariableId, out var pair) || pair.Current == null || pair.Current.DeviceId != deviceId)
            return false;

        var current = pair.Current;
        if (condition.Operator == IotAlarmOperatorEnum.StatusEqual)
            return condition.Status.HasValue && current.Status == condition.Status.Value;
        if (condition.Operator == IotAlarmOperatorEnum.StatusNotEqual)
            return condition.Status.HasValue && current.Status != condition.Status.Value;

        if (!TryNumber(current.Value, out var value) || !condition.Value.HasValue) return false;
        var threshold = condition.Value.Value;
        switch (condition.Operator)
        {
            case IotAlarmOperatorEnum.GreaterThan: return value > threshold;
            case IotAlarmOperatorEnum.GreaterOrEqual: return value >= threshold;
            case IotAlarmOperatorEnum.LessThan: return value < threshold;
            case IotAlarmOperatorEnum.LessOrEqual: return value <= threshold;
            case IotAlarmOperatorEnum.Equal: return Math.Abs(value - threshold) < 0.0000001;
            case IotAlarmOperatorEnum.NotEqual: return Math.Abs(value - threshold) >= 0.0000001;
            case IotAlarmOperatorEnum.Between:
            case IotAlarmOperatorEnum.Outside:
                if (!condition.SecondValue.HasValue) return false;
                var lower = Math.Min(threshold, condition.SecondValue.Value);
                var upper = Math.Max(threshold, condition.SecondValue.Value);
                var inside = value >= lower && value <= upper;
                return condition.Operator == IotAlarmOperatorEnum.Between ? inside : !inside;
            case IotAlarmOperatorEnum.RateGreater:
            case IotAlarmOperatorEnum.RateLess:
            case IotAlarmOperatorEnum.RateAbsoluteGreater:
                if (pair.Previous == null || !TryNumber(pair.Previous.Value, out var previousValue)) return false;
                var minutes = (current.Timestamp - pair.Previous.Timestamp).TotalMinutes;
                if (minutes <= 0) return false;
                var rate = (value - previousValue) / minutes;
                return condition.Operator == IotAlarmOperatorEnum.RateGreater ? rate > threshold
                    : condition.Operator == IotAlarmOperatorEnum.RateLess ? rate < threshold
                    : Math.Abs(rate) > threshold;
            default:
                return false;
        }
    }

    private async Task CreateEventAsync(RuleState state, DateTime timestamp, CancellationToken token)
    {
        var now = timestamp == default ? DateTime.Now : timestamp;
        var snapshot = BuildTriggerSnapshot(state.Rule);
        var alarmEvent = new IotAlarmEvent
        {
            RuleId = state.Rule.Id,
            RuleName = state.Rule.Name,
            OrgId = state.Rule.OrgId,
            DeviceId = state.Rule.DeviceId,
            DeviceName = snapshot.DeviceName,
            Severity = state.Rule.Severity,
            Status = IotAlarmEventStatusEnum.Active,
            TriggerTime = now,
            LastTriggerTime = now,
            TriggerCount = state.TriggerStreak,
            TriggerValue = JsonConvert.SerializeObject(snapshot.Values),
            Message = snapshot.Message,
            LastNotifyTime = now,
            CreateTime = DateTime.Now
        };
        await _db.Insertable(alarmEvent).ExecuteCommandAsync();
        state.ActiveEvent = alarmEvent;
        state.RecoveryStreak = 0;
        QueueNotification(alarmEvent, state.Rule.ChannelIds, IotAlarmNotificationTypeEnum.Trigger);
        _logger.LogWarning("告警触发：{Rule}/{Device}，事件 {EventId}", state.Rule.Name, alarmEvent.DeviceName, alarmEvent.Id);
    }

    private async Task UpdateActiveEventAsync(RuleState state, DateTime timestamp, CancellationToken token)
    {
        var alarmEvent = state.ActiveEvent;
        alarmEvent.LastTriggerTime = timestamp;
        alarmEvent.TriggerCount++;
        var suppressSeconds = Math.Max(state.Rule.SuppressSeconds, 0);
        if (suppressSeconds == 0 || (alarmEvent.LastNotifyTime.HasValue && (timestamp - alarmEvent.LastNotifyTime.Value).TotalSeconds < suppressSeconds))
            return;

        alarmEvent.LastNotifyTime = timestamp;
        await _db.Updateable<IotAlarmEvent>()
            .SetColumns(u => new IotAlarmEvent
            {
                LastTriggerTime = timestamp,
                TriggerCount = alarmEvent.TriggerCount,
                LastNotifyTime = timestamp,
                UpdateTime = DateTime.Now
            })
            .Where(u => u.Id == alarmEvent.Id)
            .ExecuteCommandAsync();
        QueueNotification(alarmEvent, state.Rule.ChannelIds, IotAlarmNotificationTypeEnum.Reminder);
    }

    private async Task RecoverEventAsync(IotAlarmEvent alarmEvent, DateTime timestamp, List<long> channelIds, bool notify, CancellationToken token = default)
    {
        alarmEvent.Status = IotAlarmEventStatusEnum.Recovered;
        alarmEvent.RecoverTime = timestamp;
        alarmEvent.LastNotifyTime = notify ? timestamp : alarmEvent.LastNotifyTime;
        await _db.Updateable<IotAlarmEvent>()
            .SetColumns(u => new IotAlarmEvent
            {
                Status = IotAlarmEventStatusEnum.Recovered,
                RecoverTime = timestamp,
                LastTriggerTime = alarmEvent.LastTriggerTime,
                TriggerCount = alarmEvent.TriggerCount,
                LastNotifyTime = alarmEvent.LastNotifyTime,
                UpdateTime = DateTime.Now
            })
            .Where(u => u.Id == alarmEvent.Id && (u.Status == IotAlarmEventStatusEnum.Active || u.Status == IotAlarmEventStatusEnum.Acknowledged))
            .ExecuteCommandAsync();
        if (notify) QueueNotification(alarmEvent, channelIds, IotAlarmNotificationTypeEnum.Recovery);
        _logger.LogInformation("告警恢复：{Rule}/{Device}，事件 {EventId}", alarmEvent.RuleName, alarmEvent.DeviceName, alarmEvent.Id);
    }

    private void QueueNotification(IotAlarmEvent alarmEvent, List<long> channelIds, IotAlarmNotificationTypeEnum type)
    {
        if (channelIds == null || channelIds.Count == 0) return;
        var snapshot = JsonConvert.DeserializeObject<IotAlarmEvent>(JsonConvert.SerializeObject(alarmEvent));
        var work = new NotificationWork
        {
            Event = snapshot,
            ChannelIds = channelIds.Distinct().ToList(),
            Type = type
        };
        if (!_notifications.Writer.TryWrite(work)) _logger.LogWarning("告警通知队列已满，事件 {EventId} 的 {Type} 通知被丢弃", alarmEvent.Id, type);
    }

    private async Task NotificationLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var work in _notifications.Reader.ReadAllAsync(token))
            {
                try
                {
                    await ProcessNotificationAsync(work, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 单个渠道或审计写库失败不能终止后续告警通知。
                    _logger.LogError(ex, "处理告警通知失败：事件 {EventId}，类型 {Type}", work.Event.Id, work.Type);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }

    private async Task ProcessNotificationAsync(NotificationWork work, CancellationToken token)
    {
        var channels = await _db.Queryable<IotAlarmChannel>()
            .Where(u => work.ChannelIds.Contains(u.Id) && u.Enabled)
            .ToListAsync();
        foreach (var channel in channels)
        {
            IotAlarmNotificationResult result;
            try
            {
                result = await _notifier.SendAsync(channel, work.Event, work.Type, token);
            }
            catch (Exception ex)
            {
                result = new IotAlarmNotificationResult { IsSuccess = false, Content = work.Event.Message, Response = ex.Message };
            }

            try
            {
                await _db.Insertable(new IotAlarmNotificationLog
                {
                    OrgId = work.Event.OrgId,
                    EventId = work.Event.Id,
                    RuleId = work.Event.RuleId,
                    ChannelId = channel.Id,
                    ChannelName = channel.Name,
                    ChannelType = channel.Type,
                    NotificationType = work.Type,
                    IsSuccess = result.IsSuccess,
                    SendTime = DateTime.Now,
                    Content = result.Content ?? work.Event.Message,
                    Response = result.Response,
                    CreateTime = DateTime.Now
                }).ExecuteCommandAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入告警通知审计失败：事件 {EventId}，渠道 {ChannelId}", work.Event.Id, channel.Id);
            }
        }
    }

    private TriggerSnapshot BuildTriggerSnapshot(IotAlarmRule rule)
    {
        var values = new Dictionary<string, object>();
        var parts = new List<string>();
        string deviceName = null;
        foreach (var condition in rule.Conditions)
        {
            if (!_latestSamples.TryGetValue(condition.VariableId, out var pair) || pair.Current == null) continue;
            deviceName ??= pair.Current.DeviceName;
            values[pair.Current.VariableName] = new { pair.Current.Value, Status = pair.Current.Status.ToString(), pair.Current.Timestamp };
            parts.Add($"{pair.Current.VariableName} {condition.Operator} {condition.Value?.ToString(CultureInfo.InvariantCulture)}，当前 {Convert.ToString(pair.Current.Value, CultureInfo.InvariantCulture)}");
        }
        return new TriggerSnapshot
        {
            DeviceName = deviceName ?? rule.DeviceName ?? rule.DeviceId.ToString(),
            Values = values,
            Message = $"{rule.Name}：{string.Join("；", parts)}"
        };
    }

    private static bool TryNumber(object input, out double value)
    {
        value = 0;
        if (input == null) return false;
        try
        {
            if (input is string text)
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            value = Convert.ToDouble(input, CultureInfo.InvariantCulture);
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
        catch
        {
            return false;
        }
    }

    private sealed class RuleState
    {
        public IotAlarmRule Rule { get; set; }
        public IotAlarmEvent ActiveEvent { get; set; }
        public int TriggerStreak { get; set; }
        public int RecoveryStreak { get; set; }
    }

    private sealed class SamplePair
    {
        public IotAlarmSample Previous { get; set; }
        public IotAlarmSample Current { get; set; }
    }

    private sealed class NotificationWork
    {
        public IotAlarmEvent Event { get; set; }
        public List<long> ChannelIds { get; set; }
        public IotAlarmNotificationTypeEnum Type { get; set; }
    }

    private sealed class TriggerSnapshot
    {
        public string DeviceName { get; set; }
        public Dictionary<string, object> Values { get; set; } = new();
        public string Message { get; set; }
    }
}
