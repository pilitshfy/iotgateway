using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Admin.NET.Application;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using PluginInterface;

namespace Admin.NET.Web.Core;

/// <summary>
/// 已加载的驱动信息
/// </summary>
public class IotDriverInfo
{
    public string FileName { get; set; }

    public Type Type { get; set; }
}

/// <summary>
/// 驱动插件管理（扫描 drivers/net8.0 目录）
/// </summary>
public class IotDriverManager : IIotDriverProvider
{
    private readonly object _lock = new();
    private readonly ILogger<IotDriverManager> _logger;
    private readonly string _driverPath;
    private readonly List<IotDriverInfo> _driverInfos = new();

    public IReadOnlyList<IotDriverInfo> DriverInfos => _driverInfos;

    public IotDriverManager(ILogger<IotDriverManager> logger)
    {
        _logger = logger;
        _driverPath = Path.Combine(AppContext.BaseDirectory, "drivers", "net8.0");
        try
        {
            Directory.CreateDirectory(_driverPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建驱动目录失败：{Path}", _driverPath);
        }

        LoadAllDrivers();
    }

    /// <summary>
    /// 可用驱动文件（用于前端下拉）
    /// </summary>
    public List<IotDriverFileDto> GetDriverFiles()
    {
        lock (_lock)
        {
            return _driverInfos.Select(x => new IotDriverFileDto
            {
                FileName = x.FileName,
                AssembleName = x.Type.FullName,
                DriverName = x.Type.GetCustomAttribute<DriverInfoAttribute>()?.Name ?? x.Type.Name,
                Version = x.Type.GetCustomAttribute<DriverInfoAttribute>()?.Version
            }).ToList();
        }
    }

    /// <summary>
    /// 驱动通讯参数
    /// </summary>
    public List<IotDriverConfigDto> GetDriverConfigs(string assembleName)
    {
        var type = FindType(assembleName);
        if (type == null) return new List<IotDriverConfigDto>();

        var driver = CreateDriver(string.Empty, assembleName);
        try
        {
            return type.GetProperties()
                .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<ConfigParameterAttribute>() })
                .Where(x => x.Attribute != null)
                .Select(x => new IotDriverConfigDto
                {
                    Name = x.Property.Name,
                    Type = x.Property.PropertyType.Name,
                    Description = x.Attribute.Description,
                    DefaultValue = driver == null ? null : x.Property.GetValue(driver)?.ToString(),
                    EnumInfo = x.Property.PropertyType.IsEnum
                        ? JsonConvert.SerializeObject(Enum.GetValues(x.Property.PropertyType).Cast<object>()
                            .ToDictionary(v => Enum.GetName(x.Property.PropertyType, v), v => Convert.ToInt32(v)))
                        : null
                })
                .ToList();
        }
        finally
        {
            (driver as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// 驱动方法
    /// </summary>
    public List<IotDriverMethodDto> GetDriverMethods(string assembleName)
    {
        var type = FindType(assembleName);
        if (type == null) return new List<IotDriverMethodDto>();

        return type.GetMethods()
            .Select(m => new { Method = m, Attribute = m.GetCustomAttribute<MethodAttribute>() })
            .Where(x => x.Attribute != null)
            .Select(x => new IotDriverMethodDto
            {
                Name = x.Method.Name,
                DisplayName = string.IsNullOrWhiteSpace(x.Attribute.Name) ? x.Method.Name : x.Attribute.Name,
                Description = x.Attribute.Description,
                ProtectType = (int)x.Attribute.Protect_Type
            })
            .ToList();
    }

    /// <summary>
    /// 重新扫描驱动目录
    /// </summary>
    public void ReloadDrivers()
    {
        lock (_lock)
        {
            _driverInfos.Clear();
        }

        LoadAllDrivers();
    }

    private Type FindType(string assembleName)
    {
        lock (_lock)
        {
            return _driverInfos.FirstOrDefault(x => x.Type.FullName == assembleName)?.Type;
        }
    }

    public string GetAssembleNameByFileName(string fileName)
    {
        return _driverInfos.FirstOrDefault(x => string.Equals(x.FileName, fileName, StringComparison.OrdinalIgnoreCase))?.Type.FullName;
    }

    /// <summary>
    /// 创建驱动实例（约定构造函数：string deviceName, ILogger logger）
    /// </summary>
    public IDriver CreateDriver(string deviceName, string assembleName)
    {
        var info = _driverInfos.FirstOrDefault(x => x.Type.FullName == assembleName);
        if (info == null)
        {
            _logger.LogWarning("未找到驱动：{AssembleName}", assembleName);
            return null;
        }

        var constructor = info.Type.GetConstructor(new[] { typeof(string), typeof(ILogger) });
        if (constructor == null)
        {
            _logger.LogWarning("驱动 {Type} 缺少 (string, ILogger) 构造函数", info.Type.FullName);
            return null;
        }

        try
        {
            return constructor.Invoke(new object[] { deviceName, _logger }) as IDriver;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建驱动实例失败：{Type}", info.Type.FullName);
            return null;
        }
    }

    private void LoadAllDrivers()
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(_driverPath, "*.dll");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扫描驱动目录失败：{Path}", _driverPath);
            return;
        }

        foreach (var file in files)
        {
            try
            {
                var dll = Assembly.LoadFrom(file);

                Type[] types;
                try
                {
                    types = dll.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // 某些驱动含平台相关结构体，部分类型无法加载时跳过即可
                    types = ex.Types.Where(x => x != null).ToArray();
                }

                foreach (var type in types.Where(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract))
                {
                    lock (_lock)
                    {
                        if (_driverInfos.Any(x => x.Type == type)) continue;
                        _driverInfos.Add(new IotDriverInfo { FileName = Path.GetFileName(file), Type = type });
                    }
                    _logger.LogInformation("加载驱动：{File} -> {Type}", Path.GetFileName(file), type.FullName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载驱动程序集失败：{File}", file);
            }
        }

        _logger.LogInformation("驱动加载完成，共 {Count} 个", _driverInfos.Count);
    }
}
