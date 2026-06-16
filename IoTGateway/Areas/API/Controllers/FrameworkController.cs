using Microsoft.AspNetCore.Mvc;
using WalkingTec.Mvvm.Mvc;

namespace IoTGateway.Areas.API.Controllers
{
    [Area("API")]
    [Route("api/[controller]")]
    [ApiController]
    [AuthorizeJwt]
    public class FrameworkController : BaseApiController
    {
        [HttpGet("GetCurrentUserInfo")]
        public ActionResult GetCurrentUserInfo()
        {
            if (Wtm.LoginUserInfo == null)
            {
                return JsonMore(new { Code = 401, Msg = "未登录" });
            }

            return JsonMore(new
            {
                Code = 200,
                Data = new
                {
                    Name = Wtm.LoginUserInfo.Name,
                    ITCode = Wtm.LoginUserInfo.ITCode,
                    TenantCode = Wtm.LoginUserInfo.TenantCode,
                    PhotoId = Wtm.LoginUserInfo.PhotoId
                }
            });
        }

        [HttpGet("Menu")]
        public ActionResult Menu()
        {
            // 返回简单菜单结构，前端可以根据此构建侧边栏
            var menus = new[]
            {
                new {
                    name = "dashboard",
                    title = "首页",
                    icon = "HomeFilled",
                    path = "/dashboard"
                },
                new {
                    name = "device",
                    title = "设备管理",
                    icon = "Connection",
                    path = "/device"
                },
                new {
                    name = "driver",
                    title = "驱动管理",
                    icon = "Setting",
                    path = "/driver"
                },
                new {
                    name = "config",
                    title = "系统配置",
                    icon = "Tools",
                    path = "/config"
                },
                new {
                    name = "mqtt",
                    title = "MQTT客户端",
                    icon = "Message",
                    path = "/mqtt"
                },
                new {
                    name = "rpc",
                    title = "RPC日志",
                    icon = "Document",
                    path = "/rpc"
                }
            };

            return JsonMore(new { Code = 200, Data = menus });
        }
    }
}
