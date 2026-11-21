namespace Admin.NET.Application;

/// <summary>
/// 驱动文件信息
/// </summary>
public class IotDriverFileDto
{
    /// <summary>
    /// 文件名
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// 程序集名
    /// </summary>
    public string AssembleName { get; set; }

    /// <summary>
    /// 驱动名称
    /// </summary>
    public string DriverName { get; set; }

    /// <summary>
    /// 版本
    /// </summary>
    public string Version { get; set; }
}

/// <summary>
/// 驱动通讯参数信息（对应驱动类 ConfigParameter 特性）
/// </summary>
public class IotDriverConfigDto
{
    public string Name { get; set; }

    public string Type { get; set; }

    public string Description { get; set; }

    public string DefaultValue { get; set; }

    /// <summary>
    /// 枚举可选值（JSON）
    /// </summary>
    public string EnumInfo { get; set; }
}

/// <summary>
/// 驱动方法信息（对应驱动类 Method 特性）
/// </summary>
public class IotDriverMethodDto
{
    /// <summary>
    /// 方法名（变量 Method 字段存储该值）
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName { get; set; }

    public string Description { get; set; }

    public int ProtectType { get; set; }
}

/// <summary>
/// 驱动提供器（由 Web.Core 实现），用于发现驱动文件与元数据
/// </summary>
public interface IIotDriverProvider
{
    /// <summary>
    /// 已加载的驱动文件列表
    /// </summary>
    List<IotDriverFileDto> GetDriverFiles();

    /// <summary>
    /// 驱动通讯参数列表
    /// </summary>
    List<IotDriverConfigDto> GetDriverConfigs(string assembleName);

    /// <summary>
    /// 驱动方法列表
    /// </summary>
    List<IotDriverMethodDto> GetDriverMethods(string assembleName);

    /// <summary>
    /// 重新扫描驱动目录
    /// </summary>
    void ReloadDrivers();
}
