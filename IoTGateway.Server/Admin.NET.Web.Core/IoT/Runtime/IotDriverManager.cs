using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
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
public class IotDriverManager
{
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
    public IEnumerable<object> GetDriverFiles()
    {
        return _driverInfos.Select(x => new
        {
            FileName = x.FileName,
            AssembleName = x.Type.FullName,
            DriverName = x.Type.GetCustomAttribute<DriverInfoAttribute>()?.Name ?? x.Type.Name,
            Version = x.Type.GetCustomAttribute<DriverInfoAttribute>()?.Version
        });
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
                foreach (var type in dll.GetTypes().Where(x => typeof(IDriver).IsAssignableFrom(x) && x.IsClass && !x.IsAbstract))
                {
                    _driverInfos.Add(new IotDriverInfo { FileName = Path.GetFileName(file), Type = type });
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
