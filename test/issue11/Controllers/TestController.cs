using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace issue11.Controllers
{
    [ApiController]
    public class MainController : ControllerBase
    {
        private readonly IBackgroundTaskQueue _taskQueue;

        public MainController(IBackgroundTaskQueue taskQueue)
        {
            _taskQueue = taskQueue;
        }

        [Route("[action]")]
        public async Task<ActionResult> Test()
        {
            await _taskQueue.EnqueueAsync(new CreateCollectionBackgroundWork.Order(), HttpContext.RequestAborted);

            return Ok();
        }
    }
}
