using System.ComponentModel;
using Furion.FriendlyException;
using SqlSugar;

namespace Admin.NET.Application;

/// <summary>
/// IoT 驱动服务
/// </summary>
[ApiDescriptionSettings(Order = 451)]
public class IotDriverService : IDynamicApiController, ITransient
{
    private readonly SqlSugarRepository<IotDriver> _driverRep;
    private readonly SqlSugarRepository<IotDevice> _deviceRep;

    public IotDriverService(SqlSugarRepository<IotDriver> driverRep, SqlSugarRepository<IotDevice> deviceRep)
    {
        _driverRep = driverRep;
        _deviceRep = deviceRep;
    }

    /// <summary>
    /// 驱动分页列表
    /// </summary>
    [DisplayName("驱动分页列表"), HttpPost]
    public async Task<SqlSugarPagedList<IotDriver>> Page([FromBody] IotDriverPageInput input)
    {
        return await _driverRep.AsQueryable()
            .WhereIF(!string.IsNullOrWhiteSpace(input.Keyword), u => u.DriverName.Contains(input.Keyword))
            .OrderBy(u => u.DriverName)
            .ToPagedListAsync(input.Page, input.PageSize);
    }

    /// <summary>
    /// 驱动列表
    /// </summary>
    [DisplayName("驱动列表"), HttpGet]
    public async Task<List<IotDriver>> List()
    {
        return await _driverRep.AsQueryable().OrderBy(u => u.DriverName).ToListAsync();
    }

    /// <summary>
    /// 新增驱动
    /// </summary>
    [DisplayName("新增驱动"), ApiDescriptionSettings(Name = "Add"), HttpPost]
    public async Task<long> Add([FromBody] IotDriver input)
    {
        input.Id = 0;
        await _driverRep.InsertAsync(input);
        return input.Id;
    }

    /// <summary>
    /// 修改驱动
    /// </summary>
    [DisplayName("修改驱动"), ApiDescriptionSettings(Name = "Update"), HttpPost]
    public async Task Update([FromBody] IotDriver input)
    {
        var db = await _driverRep.GetByIdAsync(input.Id) ?? throw Oops.Oh("驱动不存在");
        db.DriverName = input.DriverName;
        db.FileName = input.FileName;
        db.AssembleName = input.AssembleName;
        db.AuthorizesNum = input.AuthorizesNum;
        await _driverRep.AsUpdateable(db).ExecuteCommandAsync();
    }

    /// <summary>
    /// 删除驱动
    /// </summary>
    [DisplayName("删除驱动"), ApiDescriptionSettings(Name = "Delete"), HttpPost]
    public async Task Delete([FromBody] IotIdInput input)
    {
        var used = await _deviceRep.AsQueryable().AnyAsync(u => u.DriverId == input.Id);
        if (used) throw Oops.Oh("该驱动已被设备使用，无法删除");

        await _driverRep.DeleteByIdAsync(input.Id);
    }

    /// <summary>
    /// 批量删除驱动
    /// </summary>
    [DisplayName("批量删除驱动"), ApiDescriptionSettings(Name = "BatchDelete"), HttpPost]
    public async Task BatchDelete([FromBody] IotIdsInput input)
    {
        if (input?.Ids == null || input.Ids.Count == 0) return;

        var used = await _deviceRep.AsQueryable().AnyAsync(u => u.DriverId != null && input.Ids.Contains(u.DriverId.Value));
        if (used) throw Oops.Oh("部分驱动已被设备使用，无法删除");

        await _driverRep.Context.Deleteable<IotDriver>().In(input.Ids).ExecuteCommandAsync();
    }
}
