using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Admin.NET.Application;
using ModelContextProtocol.Server;

namespace Admin.NET.Web.Core;

/// <summary>
/// IoT MCP 工具（对齐原 IoTGateway MCP/DeviceTool）
/// </summary>
[McpServerToolType]
public sealed class IotMcpTools
{
    private readonly IIotDeviceRuntime _runtime;

    public IotMcpTools(IIotDeviceRuntime runtime)
    {
        _runtime = runtime;
    }

    /// <summary>
    /// 获取子设备列表
    /// </summary>
    [McpServerTool(Name = "DevicesList"), Description("Get the list of sub-devices.")]
    public IEnumerable<string> DevicesList()
    {
        return _runtime.GetDeviceNames();
    }

    /// <summary>
    /// 获取子设备当前连接状态
    /// </summary>
    [McpServerTool, Description("Get the current connection status of the sub-device.")]
    public bool? GetDeviceStatus([Description("name of device")] string deviceName)
    {
        return _runtime.GetDeviceStatus(deviceName);
    }

    /// <summary>
    /// 获取子设备全部变量的当前值
    /// </summary>
    [McpServerTool, Description("Get sub-device variables.")]
    public Dictionary<string, object> GetDeviceVariables([Description("name of device")] string deviceName)
    {
        return _runtime.GetDeviceVariablesByName(deviceName)
            .ToDictionary(x => x.Name, x => x.CookedValue);
    }

    /// <summary>
    /// 获取子设备指定变量的当前值
    /// </summary>
    [McpServerTool, Description("Get the current value of a variable of a sub-device.")]
    public object GetDeviceVariable(
        [Description("name of device")] string deviceName,
        [Description("name of variable")] string variableName)
    {
        return _runtime.GetDeviceVariablesByName(deviceName)
            .FirstOrDefault(x => x.Name == variableName)?.CookedValue;
    }

    /// <summary>
    /// 连接设备
    /// </summary>
    [McpServerTool, Description("Connect to a device.")]
    public bool ConnectDevice([Description("name of device")] string deviceName)
    {
        return _runtime.ConnectDevice(deviceName);
    }

    /// <summary>
    /// 断开设备
    /// </summary>
    [McpServerTool, Description("Disconnect from a device.")]
    public bool DisconnectDevice([Description("name of device")] string deviceName)
    {
        return _runtime.DisconnectDevice(deviceName);
    }

    /// <summary>
    /// 重启设备连接
    /// </summary>
    [McpServerTool, Description("Restart device connection.")]
    public async Task<bool> RestartDevice([Description("name of device")] string deviceName)
    {
        return await _runtime.RestartDeviceByNameAsync(deviceName);
    }

    /// <summary>
    /// 获取设备详细信息
    /// </summary>
    [McpServerTool, Description("Get detailed device information.")]
    public Dictionary<string, object> GetDeviceInfo([Description("name of device")] string deviceName)
    {
        return _runtime.GetDeviceInfo(deviceName);
    }

    /// <summary>
    /// 获取设备驱动信息
    /// </summary>
    [McpServerTool, Description("Get device driver information.")]
    public async Task<string> GetDeviceDriver([Description("name of device")] string deviceName)
    {
        return await _runtime.GetDeviceDriverNameAsync(deviceName);
    }

    /// <summary>
    /// 写入设备变量
    /// </summary>
    [McpServerTool(Name = "WriteDeviceVariableAsync"), Description("Write value to a device variable.")]
    public async Task<Dictionary<string, object>> WriteDeviceVariableAsync(
        [Description("name of device")] string deviceName,
        [Description("name of variable")] string variableName,
        [Description("value to write")] object value)
    {
        var result = await _runtime.ExecuteRpcAsync(new IotRpcRequestDto
        {
            DeviceName = deviceName,
            Method = "write",
            RequestId = Guid.NewGuid().ToString(),
            Params = new Dictionary<string, object> { { variableName, value } }
        });

        return new Dictionary<string, object>
        {
            { "success", result.IsSuccess },
            { "description", result.Description ?? string.Empty }
        };
    }
}
