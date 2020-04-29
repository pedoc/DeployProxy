using System;
using System.Linq;
using System.Threading.Tasks;
using DeployProxy.Entity;
using DeployProxy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace DeployProxy.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class EventController : Controller
    {
        private readonly ILogger _logger;

        public EventController(ILogger<EventController> logger)
        {
            _logger = logger;
        }
        [HttpGet]
        public IActionResult Index()
        {
            return Content("welcome");
        }

        [HttpPost]
        public IActionResult EventNotify([FromBody] EventNotifyInput input)
        {
            Request.Headers.TryGetValue("X-Gitlab-Token", out var token);
            Request.Headers.TryGetValue("X-Gitlab-Event", out var @event);
            _logger.LogDebug($"token={token} event={@event}");
            var msg = "";
            var deployTasks = DeployTask.GetDeployTasks();
            if (deployTasks == null || !deployTasks.Any())
            {
                msg = "没有配置DeployTask";
                _logger.LogDebug(msg);
                return BadRequest(msg);
            }
            else
            {
                var name = input?.project?.name;
                if (string.IsNullOrEmpty(name)) return BadRequest("参数中缺少工程名称");
                else
                {
                    var activeDeployTasks =
                        deployTasks
                            .Where(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                    if (!activeDeployTasks.Any())
                    {
                        msg = $"没有与{name}对应的有效DeployTask";
                        _logger.LogDebug(msg);
                        return BadRequest(msg);
                    }
                    else
                    {
                        foreach (var deployTask in activeDeployTasks)
                        {
                            Task.Run(() =>
                            {
                                deployTask.Ensure();
                                deployTask.Execute(_logger, input).ConfigureAwait(false);
                            });
                        }
                    }
                }
            }
            return Ok();
        }
    }
}
