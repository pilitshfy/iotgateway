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
}

/// <summary>
/// 驱动分页查询
/// </summary>
public class IotDriverPageInput : BasePageInput
{
    public string Keyword { get; set; }
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
/// RPC 日志分页查询
/// </summary>
public class IotRpcLogPageInput : BasePageInput
{
    public long? DeviceId { get; set; }

    public string Keyword { get; set; }
}
