using IoTGateway.ViewModel.HomeVMs;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using WalkingTec.Mvvm.Core;
using WalkingTec.Mvvm.Core.Support.Json;
using WalkingTec.Mvvm.Mvc;

namespace IoTGateway.Areas.API.Controllers
{
    [Area("API")]
    [Route("api/[controller]")]
    [ApiController]
    public class LoginController : BaseApiController
    {
        private readonly ITokenService _tokenService;

        public LoginController(ITokenService tokenService)
        {
            _tokenService = tokenService;
        }

        [HttpPost]
        [Route("")]
        [Public]
        public async Task<ActionResult> Login([FromBody] LoginVM vm)
        {
            if (!ModelState.IsValid)
            {
                return JsonMore(new { Code = 400, Msg = "参数错误" });
            }

            var user = Wtm.DoLogin(vm.ITCode, vm.Password, vm.Tenant);
            if (user == null)
            {
                return JsonMore(new { Code = 401, Msg = "用户名或密码错误" });
            }

            Wtm.LoginUserInfo = user;
            var token = await _tokenService.IssueTokenAsync(user);

            return JsonMore(new
            {
                Code = 200,
                Msg = "登录成功",
                Data = new
                {
                    Token = token.AccessToken,
                    ExpiresIn = token.ExpiresIn,
                    UserName = user.Name,
                    ITCode = user.ITCode
                }
            });
        }

        [HttpPost]
        [Route("logout")]
        [AllRights]
        public ActionResult Logout()
        {
            HttpContext.Session.Clear();
            return JsonMore(new { Code = 200, Msg = "登出成功" });
        }

        [HttpGet]
        [Route("currentuser")]
        [AllRights]
        public ActionResult GetCurrentUser()
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
                    TenantCode = Wtm.LoginUserInfo.TenantCode
                }
            });
        }
    }
}
