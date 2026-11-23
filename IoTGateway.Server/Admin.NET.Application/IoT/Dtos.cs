using System.ComponentModel.DataAnnotations;

namespace Admin.NET.Application;

/// <summary>
/// 主键输入
/// </summary>
public class IotIdInput
{
    [Required(ErrorMessage = "Id不能为空")]
    public long Id { get; set; }
}

/// <summary>
/// 批量主键输入
/// </summary>
public class IotIdsInput
{
    public List<long> Ids { get; set; } = new();
}

/// <summary>
/// 设备复制输入
/// </summary>
public class IotDeviceCopyInput
{
    [Required(ErrorMessage = "设备Id不能为空")]
    public long Id { get; set; }

    [Range(1, 100, ErrorMessage = "复制数量范围为1-100")]
    public int Count { get; set; } = 1;
}

/// <summary>
/// 变量批量写入输入
/// </summary>
public class IotVariableWriteInput
{
    [Required(ErrorMessage = "设备Id不能为空")]
    public long DeviceId { get; set; }

    [Required(ErrorMessage = "写入值不能为空")]
    public Dictionary<string, object> Values { get; set; } = new();
}

/// <summary>
/// 设备分页查询
/// </summary>
public class IotDevicePageInput : BasePageInput
{
    /// <summary>
    /// 关键字（设备名）
    /// </summary>
    public string Keyword { get; set; }

    /// <summary>
    /// 驱动Id
    /// </summary>
    public long? DriverId { get; set; }

    /// <summary>
    /// 设备类型
    /// </summary>
    public IotDeviceTypeEnum? DeviceTypeEnum { get; set; }

    /// <summary>
    /// 父级Id
    /// </summary>
    public long? ParentId { get; set; }

    /// <summary>
    /// 所属组织Id（包含子组织）
    /// </summary>
    public long? OrgId { get; set; }
}

/// <summary>
/// 驱动分页查询
/// </summary>
public class IotDriverPageInput : BasePageInput
{
    public string Keyword { get; set; }
}

/// <summary>
/// 边缘节点分页查询
/// </summary>
public class IotEdgeNodePageInput : BasePageInput
{
    /// <summary>关键字（名称/编号）</summary>
    public string Keyword { get; set; }

    /// <summary>所属组织Id（含子组织）</summary>
    public long? OrgId { get; set; }

    /// <summary>在线状态筛选</summary>
    public bool? Online { get; set; }
}

/// <summary>
/// 边缘节点心跳上报输入
/// </summary>
public class IotEdgeNodeHeartbeatInput
{
    /// <summary>节点编号（唯一寻址）</summary>
    [Required(ErrorMessage = "节点编号不能为空")]
    public string NodeCode { get; set; }

    /// <summary>节点固件/程序版本</summary>
    public string Version { get; set; }
}

/// <summary>
/// 边缘节点下发输入（配置/指令）
/// </summary>
public class IotEdgeNodeSendInput
{
    /// <summary>节点Id</summary>
    public long Id { get; set; }

    /// <summary>下发类型：config（配置，retain 持久化）/ command（指令）</summary>
    public string Kind { get; set; } = "config";

    /// <summary>下发内容（JSON 字符串或纯文本）</summary>
    [Required(ErrorMessage = "下发内容不能为空")]
    public string Payload { get; set; }
}

/// <summary>
/// 通讯配置分页查询
/// </summary>
public class IotDeviceConfigPageInput : BasePageInput
{
    public long? DeviceId { get; set; }

    public string Keyword { get; set; }
}

/// <summary>
/// 变量分页查询
/// </summary>
public class IotDeviceVariablePageInput : BasePageInput
{
    public long? DeviceId { get; set; }

    public string Keyword { get; set; }
}

/// <summary>
/// 设备批量修改输入（仅更新有值字段）
/// </summary>
public class IotDeviceBatchUpdateInput
{
    public List<long> Ids { get; set; } = new();

    public long? DriverId { get; set; }

    public long? ParentId { get; set; }

    public long? OrgId { get; set; }

    public bool? AutoStart { get; set; }

    public bool? CgUpload { get; set; }

    public int? EnforcePeriod { get; set; }

    public int? CmdPeriod { get; set; }
}

/// <summary>
/// 变量批量修改输入（仅更新有值字段）
/// </summary>
public class IotDeviceVariableBatchUpdateInput
{
    public List<long> Ids { get; set; } = new();

    public string Method { get; set; }

    public IotDataTypeEnum? DataType { get; set; }

    public IotEndianEnum? EndianType { get; set; }

    public bool? IsTrigger { get; set; }

    public bool? IsUpload { get; set; }

    public IotProtectTypeEnum? ProtectType { get; set; }

    public string Alias { get; set; }

    public string Expressions { get; set; }

    public int? Index { get; set; }
}

/// <summary>
/// 通讯配置批量修改输入（仅更新有值字段）
/// </summary>
public class IotDeviceConfigBatchUpdateInput
{
    public List<long> Ids { get; set; } = new();

    public IotDataSideEnum? DataSide { get; set; }

    public string Value { get; set; }

    public string Description { get; set; }
}

/// <summary>
/// 驱动批量修改输入（仅更新有值字段）
/// </summary>
public class IotDriverBatchUpdateInput
{
    public List<long> Ids { get; set; } = new();

    public string FileName { get; set; }

    public string AssembleName { get; set; }

    public int? AuthorizesNum { get; set; }
}

/// <summary>
/// 图表数据项
/// </summary>
public class IotChartDataDto
{
    /// <summary>
    /// 分类
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// 系列
    /// </summary>
    public string Series { get; set; }

    /// <summary>
    /// 数值
    /// </summary>
    public int Value { get; set; }
}

/// <summary>
/// 设备变量状态图表
/// </summary>
public class IotVariableChartDto
{
    /// <summary>
    /// 设备名称
    /// </summary>
    public string DeviceName { get; set; }

    /// <summary>
    /// 正常变量数
    /// </summary>
    public int GoodCount { get; set; }

    /// <summary>
    /// 异常变量数
    /// </summary>
    public int BadCount { get; set; }
}

/// <summary>
/// RPC 日志分页查询
/// </summary>
public class IotRpcLogPageInput : BasePageInput
{
    public long? DeviceId { get; set; }

    public string Keyword { get; set; }
}
