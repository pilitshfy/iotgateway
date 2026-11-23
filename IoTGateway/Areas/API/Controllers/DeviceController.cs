using IoTGateway.ViewModel.BasicData.DeviceVMs;
using Microsoft.AspNetCore.Mvc;
using System;
using WalkingTec.Mvvm.Core;
using WalkingTec.Mvvm.Core.Extensions;
using WalkingTec.Mvvm.Mvc;

namespace IoTGateway.Areas.API.Controllers
{
    [Area("API")]
    [Route("api/[controller]")]
    [ApiController]
    [AuthorizeJwt]
    public class DeviceController : BaseApiController
    {
        [HttpPost("Search")]
        public ActionResult Search([FromBody] DeviceSearcher searcher)
        {
            var vm = Wtm.CreateVM<DeviceListVM>(passInit: true);
            if (ModelState.IsValid)
            {
                vm.Searcher = searcher;
                var result = vm.GetJsonForApi();
                return JsonMore(new { Code = 200, Data = result });
            }
            else
            {
                return JsonMore(new { Code = 400, Msg = "参数错误" });
            }
        }

        [HttpGet("{id}")]
        public ActionResult Get(string id)
        {
            var vm = Wtm.CreateVM<DeviceVM>(id);
            return JsonMore(new { Code = 200, Data = vm.Entity });
        }

        [HttpPost("Create")]
        public ActionResult Create([FromBody] DeviceVM vm)
        {
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "参数错误" });
            }
            vm.DoAdd();
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "创建失败" });
            }
            return JsonMore(new { Code = 200, Msg = "创建成功", Data = vm.Entity });
        }

        [HttpPost("Edit")]
        public ActionResult Edit([FromBody] DeviceVM vm)
        {
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "参数错误" });
            }
            vm.DoEdit();
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "更新失败" });
            }
            return JsonMore(new { Code = 200, Msg = "更新成功", Data = vm.Entity });
        }

        [HttpDelete("{id}")]
        public ActionResult Delete(string id)
        {
            var vm = Wtm.CreateVM<DeviceVM>(id);
            vm.DoDelete();
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "删除失败" });
            }
            return JsonMore(new { Code = 200, Msg = "删除成功" });
        }

        [HttpPost("BatchDelete")]
        public ActionResult BatchDelete([FromBody] string[] ids)
        {
            var vm = Wtm.CreateVM<DeviceBatchVM>(Ids: ids);
            if (!vm.DoBatchDelete())
            {
                return JsonMore(new { Code = 400, Msg = "批量删除失败" });
            }
            return JsonMore(new { Code = 200, Msg = "批量删除成功" });
        }

        [HttpGet("GetMethods")]
        public ActionResult GetMethods(Guid? ID)
        {
            return JsonMore(new { Code = 200, Data = "" });
        }
    }
}
