using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace LMS.Master.WebApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MasterController : ControllerBase
    {
        /// <summary>
        /// Test Service
        /// </summary>
        /// <returns></returns>
        [HttpGet(nameof(TestService))]
        public string TestService()
        {
            return "I am Live";
        }
    }
}