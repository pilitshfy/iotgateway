using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Admin.NET.Application;
using DynamicExpresso;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PluginInterface;

namespace Admin.NET.Web.Core;

/// <summary>
/// 单设备采集线程（移植自原 Plugin/DeviceThread.cs）
/// </summary>
public class IotDeviceThread : IDisposable
{
    private readonly IotMqttServer _mqttServer;
    private readonly IotPlatformService _platformService;
    private readonly ILogger _logger;
    public readonly IotDevice Device;
    public readonly IDriver Driver;
    private readonly Interpreter _interpreter;

    /// <summary>
    /// 缓存所有标记 MethodAttribute 的方法委托
    /// </summary>
    public readonly Dictionary<string, Func<DriverAddressIoArgModel, DriverReturnValueModel>> MethodDelegates = new();

    private Task _task;
    private readonly DateTime _tsStartDt = new(1970, 1, 1);
    private readonly CancellationTokenSource _tokenSource = new();
    private readonly ManualResetEventSlim _resetEvent = new(true);
    private DateTime _lastGoodTime = DateTime.UtcNow;
    private readonly TimeSpan _maxFailureDuration = TimeSpan.FromSeconds(60);
    private string _lastTelemetryJson;
    private DateTime _lastPublishTime = DateTime.MinValue;
    private bool? _lastConnected;

    public IotDeviceThread(IotDevice device, IDriver driver, IotMqttServer mqttServer, IotPlatformService platformService, ILogger logger)
    {
        Device = device;
        Driver = driver;
        _mqttServer = mqttServer;
        _platformService = platformService;
        _logger = logger;
        _interpreter = new Interpreter();

        foreach (var methodInfo in Driver.GetType().GetMethods().Where(x => x.GetCustomAttribute(typeof(MethodAttribute)) != null))
        {
            try
            {
                var del = (Func<DriverAddressIoArgModel, DriverReturnValueModel>)Delegate.CreateDelegate(
                    typeof(Func<DriverAddressIoArgModel, DriverReturnValueModel>), Driver, methodInfo);
                MethodDelegates[methodInfo.Name] = del;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "无法创建驱动方法委托：{Method}", methodInfo.Name);
            }
        }

        Device.DeviceVariables ??= new List<IotDeviceVariable>();
        foreach (var item in Device.DeviceVariables)
        {
            item.StatusType = IotVariableStatusEnum.Bad;
            item.Alias ??= string.Empty;
        }
    }

    public void Start()
    {
        if (!Device.AutoStart) return;

        _logger.LogInformation("设备采集线程启动：{DeviceName}", Device.DeviceName);
        _task = CreateThreadAsync(_tokenSource.Token);
    }

    private async Task CreateThreadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(5000, token);

            while (!token.IsCancellationRequested)
            {
                _resetEvent.Wait(token);
                try
                {
                    if (Driver.IsConnected)
                    {
                        await PollAsync(token);
                    }
                    else
                    {
                        await PublishStatusAsync(false);
                        if (Driver.Connect())
                        {
                            _lastGoodTime = DateTime.UtcNow;
                            await PublishStatusAsync(true);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "设备采集循环异常：{DeviceName}", Device.DeviceName);
                }

                var period = Device.DeviceVariables.Count > 0 ? (int)Math.Max(Driver.MinPeriod, 100) : 10000;
                await Task.Delay(period, token);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("设备采集线程取消：{DeviceName}", Device.DeviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设备采集线程异常退出：{DeviceName}", Device.DeviceName);
        }
    }

    private async Task PollAsync(CancellationToken token)
    {
        foreach (var group in Device.DeviceVariables.Where(x => x.ProtectType != IotProtectTypeEnum.WriteOnly).GroupBy(x => x.Alias))
        {
            var deviceName = string.IsNullOrWhiteSpace(group.Key) ? Device.DeviceName : group.Key;
            var dictionary = new Dictionary<string, object>();

            var triggerVariables = group.Where(x => x.IsTrigger).ToList();
            await ReadVariablesAsync(triggerVariables, dictionary, token);
            var triggerValues = triggerVariables.ToDictionary(x => x.Name, x => x.CookedValue);

            if (triggerValues.Values.Any(x => x is true) || triggerVariables.Count == 0)
            {
                var nonTriggerVariables = group.Where(x => !triggerVariables.Select(y => y.Id).Contains(x.Id)).ToList();
                await ReadVariablesAsync(nonTriggerVariables, dictionary, token);

                var uploadValues = group
                    .Where(x => x.StatusType == IotVariableStatusEnum.Good && x.IsUpload)
                    .GroupBy(x => x.Name)
                    .ToDictionary(x => x.Key, x => x.First().CookedValue);
                await PublishTelemetryAsync(deviceName, uploadValues);
            }

            var hasBad = group.Any(x => x.StatusType == IotVariableStatusEnum.Bad);
            if (hasBad) await PublishStatusAsync(false);
        }

        // 连续失败超过阈值，主动断开重连
        if (Device.DeviceVariables.All(x => x.StatusType != IotVariableStatusEnum.Good))
        {
            if (DateTime.UtcNow - _lastGoodTime > _maxFailureDuration && Driver.IsConnected)
            {
                _logger.LogWarning("{DeviceName} 连续读取失败超过 {Seconds} 秒，主动断开重连", Device.DeviceName, _maxFailureDuration.TotalSeconds);
                Driver.Close();
                Driver.Dispose();
            }
        }
        else
        {
            _lastGoodTime = DateTime.UtcNow;
        }
    }

    private async Task ReadVariablesAsync(List<IotDeviceVariable> variables, Dictionary<string, object> values, CancellationToken token)
    {
        if (variables.Count == 0) return;

        foreach (var item in variables.OrderBy(x => x.Index))
        {
            var ret = new DriverReturnValueModel();

            if (item.Method == null || !MethodDelegates.TryGetValue(item.Method, out var methodDelegate))
            {
                ret.StatusType = VaribaleStatusTypeEnum.MethodError;
                ret.Message = $"未找到方法：{item.Method}";
            }
            else
            {
                var ioArg = new DriverAddressIoArgModel
                {
                    ID = Guid.Empty,
                    Address = item.DeviceAddress,
                    ValueType = (DataTypeEnum)(int)item.DataType,
                    EndianType = (EndianEnum)(int)item.EndianType
                };

                try
                {
                    ret = methodDelegate(ioArg);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "调用变量方法异常：{Device}/{Variable}", Device.DeviceName, item.Name);
                    ret.StatusType = VaribaleStatusTypeEnum.MethodError;
                    ret.Message = ex.Message;
                }
            }

            ret.Timestamp = DateTime.Now;
            item.EnqueueVariable(ret.Value);

            if (ret.StatusType == VaribaleStatusTypeEnum.Good && !string.IsNullOrWhiteSpace(item.Expressions))
            {
                var expressionText = string.Empty;
                try
                {
                    expressionText = item.Expressions.GetExpressionText(item.Values,
                        variables.DistinctBy(x => x.Name).ToDictionary(x => x.Name, x => x.Value));
                    ret.CookedValue = _interpreter.Eval(expressionText);
                }
                catch (Exception)
                {
                    ret.Message = $"表达式错误：{expressionText}";
                    ret.StatusType = VaribaleStatusTypeEnum.ExpressionError;
                }
            }
            else
            {
                ret.CookedValue = ret.Value;
            }

            item.EnqueueCookedVariable(ret.CookedValue);
            values[item.Name] = ret.CookedValue;
            item.Value = ret.Value;
            item.CookedValue = ret.CookedValue;
            item.StatusType = (IotVariableStatusEnum)(int)ret.StatusType;
            item.Timestamp = ret.Timestamp;
            item.Message = ret.Message;

            // 变量值变化时推送实时消息，供前端订阅
            var changed = JsonConvert.SerializeObject(item.Values[0]) != JsonConvert.SerializeObject(item.Values[1])
                          || JsonConvert.SerializeObject(item.CookedValues[0]) != JsonConvert.SerializeObject(item.CookedValues[1]);
            if (changed)
            {
                await _mqttServer.PublishAsync(
                    $"internal/v1/gateway/telemetry/{Device.DeviceName}/{item.Name}",
                    JsonConvert.SerializeObject(ret));
            }

            if (Device.CmdPeriod > 0)
                await Task.Delay(Device.CmdPeriod, token);
        }
    }

    private async Task PublishTelemetryAsync(string deviceName, Dictionary<string, object> values)
    {
        var json = JsonConvert.SerializeObject(values);
        var now = DateTime.UtcNow;
        var changed = json != _lastTelemetryJson;
        var elapsed = (now - _lastPublishTime).TotalMilliseconds;
        if (!Device.CgUpload || changed || elapsed >= Math.Max(Device.EnforcePeriod, 1000))
        {
            _lastTelemetryJson = json;
            _lastPublishTime = now;
            var payload = JsonConvert.SerializeObject(new
            {
                ts = (long)(DateTime.UtcNow - _tsStartDt).TotalMilliseconds,
                devicestatus = "Good",
                values
            });
            await _mqttServer.PublishAsync($"internal/v1/gateway/telemetry/{deviceName}", payload);

            if (_platformService != null)
                await _platformService.PublishTelemetryAsync(deviceName, Device, values);
        }
    }

    private async Task PublishStatusAsync(bool connected)
    {
        if (_lastConnected == connected) return;
        _lastConnected = connected;
        await _mqttServer.PublishAsync(
            $"internal/v1/gateway/status/{Device.DeviceName}",
            JsonConvert.SerializeObject(new { connected }));

        if (_platformService != null)
        {
            if (connected) await _platformService.DeviceConnectedAsync(Device.DeviceName);
            else await _platformService.DeviceDisconnectedAsync(Device.DeviceName);
        }
    }

    /// <summary>
    /// 执行 RPC（写入设备变量）
    /// </summary>
    public async Task<RpcResponse> ExecuteRpcAsync(string deviceName, string method, string requestId, Dictionary<string, object> parameters)
    {
        var response = new RpcResponse
        {
            DeviceName = deviceName,
            RequestId = requestId,
            Method = method,
            IsSuccess = false
        };

        if (!string.Equals(method, "write", StringComparison.OrdinalIgnoreCase))
        {
            response.Description = $"方法:{method}暂未实现";
            return response;
        }

        _resetEvent.Reset();
        var openedHere = false;
        try
        {
            if (!Driver.IsConnected)
            {
                openedHere = Driver.Connect();
            }

            if (!Driver.IsConnected)
            {
                response.Description = $"{deviceName} 连接失败";
                return response;
            }

            var success = true;
            var description = string.Empty;
            if (parameters == null || parameters.Count == 0)
            {
                success = false;
                description = "请求参数为空";
            }

            foreach (var para in parameters ?? new Dictionary<string, object>())
            {
                var variable = string.Equals(deviceName, Device.DeviceName, StringComparison.Ordinal)
                    ? Device.DeviceVariables.FirstOrDefault(x => x.Name == para.Key)
                    : Device.DeviceVariables.FirstOrDefault(x => x.Name == para.Key && x.Alias == deviceName);

                if (variable == null || variable.ProtectType == IotProtectTypeEnum.ReadOnly)
                {
                    success = false;
                    description += $"未能找到支持写入的变量:{para.Key},";
                    continue;
                }

                var ioArg = new DriverAddressIoArgModel
                {
                    Address = variable.DeviceAddress,
                    Value = para.Value,
                    ValueType = (DataTypeEnum)(int)variable.DataType,
                    EndianType = (EndianEnum)(int)variable.EndianType
                };

                var writeResponse = await Driver.WriteAsync(requestId, variable.Method, ioArg);
                if (!writeResponse.IsSuccess)
                {
                    success = false;
                    description += writeResponse.Description;
                }
            }

            response.IsSuccess = success;
            response.Description = description;
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RPC 写入异常：{DeviceName}", Device.DeviceName);
            response.Description = ex.Message;
            return response;
        }
        finally
        {
            if (openedHere) Driver.Close();
            _resetEvent.Set();
        }
    }

    public void StopThread()
    {
        _logger.LogInformation("设备采集线程停止：{DeviceName}", Device.DeviceName);
        _platformService?.DeviceDisconnectedAsync(Device.DeviceName).GetAwaiter().GetResult();
        _tokenSource.Cancel();
        try
        {
            Driver.Close();
        }
        catch
        {
            // ignored
        }
    }

    public void Dispose()
    {
        try
        {
            Driver.Dispose();
        }
        catch
        {
            // ignored
        }

        MethodDelegates.Clear();
        _tokenSource.Dispose();
        _resetEvent.Dispose();
    }
}
