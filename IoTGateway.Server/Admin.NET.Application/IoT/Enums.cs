using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Admin.NET.Application;

/// <summary>
/// 设备类型
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotDeviceTypeEnum
{
    [Display(Name = "分组")]
    Group = 0,

    [Display(Name = "设备")]
    Device = 1
}

/// <summary>
/// 数据侧
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotDataSideEnum
{
    [Display(Name = "任意侧")]
    AnySide = 0,

    [Display(Name = "客户端")]
    ClientSide = 2
}

/// <summary>
/// 变量数据类型
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotDataTypeEnum
{
    [Display(Name = "bit")] Bit = 0,
    [Display(Name = "bool")] Bool = 1,
    [Display(Name = "uint8")] UByte = 2,
    [Display(Name = "int8")] Byte = 3,
    [Display(Name = "uint16")] Uint16 = 4,
    [Display(Name = "int16")] Int16 = 5,
    [Display(Name = "bcd16")] Bcd16 = 6,
    [Display(Name = "uint32")] Uint32 = 7,
    [Display(Name = "int32")] Int32 = 8,
    [Display(Name = "float")] Float = 9,
    [Display(Name = "bcd32")] Bcd32 = 10,
    [Display(Name = "uint64")] Uint64 = 11,
    [Display(Name = "int64")] Int64 = 12,
    [Display(Name = "double")] Double = 13,
    [Display(Name = "ascii")] AsciiString = 14,
    [Display(Name = "utf8")] Utf8String = 15,
    [Display(Name = "datetime")] DateTime = 16,
    [Display(Name = "timestamp(ms)")] TimeStampMs = 17,
    [Display(Name = "timestamp(s)")] TimeStampS = 18,
    [Display(Name = "Any")] Any = 19,
    [Display(Name = "Custom1")] Custome1 = 20,
    [Display(Name = "Custom2")] Custome2 = 21,
    [Display(Name = "Custom3")] Custome3 = 22,
    [Display(Name = "Custom4")] Custome4 = 23,
    [Display(Name = "Custom5")] Custome5 = 24,
    [Display(Name = "Gb2312")] Gb2312String = 25
}

/// <summary>
/// 大小端
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotEndianEnum
{
    [Display(Name = "None")] None = 0,
    [Display(Name = "BigEndian")] BigEndian = 1,
    [Display(Name = "LittleEndian")] LittleEndian = 2,
    [Display(Name = "BigEndianSwap")] BigEndianSwap = 3,
    [Display(Name = "LittleEndianSwap")] LittleEndianSwap = 4
}

/// <summary>
/// 变量读写权限
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotProtectTypeEnum
{
    [Display(Name = "只读")] ReadOnly = 0,
    [Display(Name = "读写")] ReadWrite = 1,
    [Display(Name = "只写")] WriteOnly = 2
}

/// <summary>
/// 变量状态
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotVariableStatusEnum
{
    Good = 0,
    AddressError = 1,
    MethodError = 2,
    ExpressionError = 3,
    Bad = 4,
    UnKnow = 5,
    Custome1 = 6,
    Custome2 = 7,
    Custome3 = 8,
    Custome4 = 9,
    Custome5 = 10
}

/// <summary>
/// RPC 发起方
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotRpcSideEnum
{
    [Display(Name = "服务端")] ServerSide = 1,
    [Display(Name = "客户端")] ClientSide = 2
}

/// <summary>
/// 上游输出平台
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IotPlatformTypeEnum
{
    [Display(Name = "ThingsBoard")] ThingsBoard = 0,
    [Display(Name = "IoTSharp")] IoTSharp = 1,
    [Display(Name = "阿里云IoT")] AliCloudIoT = 2,
    [Display(Name = "腾讯IoTHub")] TencentIoTHub = 3,
    [Display(Name = "百度IoTCore")] BaiduIoTCore = 4,
    [Display(Name = "OneNet")] OneNET = 5,
    [Display(Name = "ThingsCloud")] ThingsCloud = 6,
    [Display(Name = "华为云")] HuaWei = 7,
    [Display(Name = "IoTGateway")] IoTGateway = 8,
    [Display(Name = "ThingsPanel")] ThingsPanel = 9
}
