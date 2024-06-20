using System.Collections.Concurrent;
using DiscordCloneServer.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class SignalingController : ControllerBase
    {
        private static ConcurrentDictionary<string, string> offers = new ConcurrentDictionary<string, string>();
        private static ConcurrentDictionary<string, string> answers = new ConcurrentDictionary<string, string>();
        private static ConcurrentQueue<string> iceCandidates = new ConcurrentQueue<string>();

        [HttpPost("offer")]
        public async Task<IActionResult> PostOffer([FromBody] SignalMessage message)
        {
            offers[message.User] = message.Data;
            return Ok();
        }

        [HttpPost("answer")]
        public async Task<IActionResult> PostAnswer([FromBody] SignalMessage message)
        {
            answers[message.User] = message.Data;
            return Ok();
        }

        [HttpPost("ice-candidate")]
        public async Task<IActionResult> PostIceCandidate([FromBody] SignalMessage message)
        {
            iceCandidates.Enqueue(message.Data);
            return Ok();
        }

        [HttpGet("offer/{user}")]
        public async Task<IActionResult> GetOffer(string user)
        {
            offers.TryGetValue(user, out var offer);
            return Ok(offer);
        }

        [HttpGet("answer/{user}")]
        public async Task<IActionResult> GetAnswer(string user)
        {
            answers.TryGetValue(user, out var answer);
            return Ok(answer);
        }

        [HttpGet("ice-candidates")]
        public async Task<IActionResult> GetIceCandidates()
        {
            return Ok(iceCandidates.ToArray());
        }
    }
}

