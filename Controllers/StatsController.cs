using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.ModCommands.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.ModCommands.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    public class StatsController : ControllerBase
    {
        /// <summary>
        /// Creates a new instance of <see cref="StatsController"/>
        /// </summary>
        public StatsController()
        {
        }

        /// <summary>
        /// Indicates status of service, should be 200 (OK)
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        [Route("/status")]
        public async Task TrackFlip()
        {

        }
    }
}
