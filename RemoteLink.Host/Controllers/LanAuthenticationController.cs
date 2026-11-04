using Microsoft.AspNetCore.Mvc;
using RemoteLink.Core.Models.Authentication;
using RemoteLink.Host.Services;

namespace RemoteLink.Host.Controllers
{
    [ApiController]
    [Route("api/lan/auth")]
    public class LanAuthenticationController(PeerAuthenticationService authentication) : ControllerBase
    {
        [HttpPost("challenge")]
        [RequestSizeLimit(4096)]
        public IActionResult CreateChallenge([FromBody] PeerChallengeRequest request)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            try
            {
                PeerChallengeResponse response = authentication.CreateChallenge(request.DeviceId);

                return Ok(response);
            }
            catch (ArgumentException)
            {
                return BadRequest("Invalid authentication request.");
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("Device authentication is not permitted.");
            }
            catch (InvalidOperationException)
            {
                return StatusCode(429, "Authentication challenge capacity reached.");
            }
        }
        
        [HttpPost("complete")]
        [RequestSizeLimit(4096)]
        public IActionResult CompleteAuthentication([FromBody] PeerAuthenticationRequest request)
        {
            if (HttpContext.Connection.LocalPort != 5019)
            {
                return NotFound();
            }

            try
            {
                PeerAuthenticationResponse response = authentication.Authenticate(request);

                return Ok(response);
            }
            catch (UnauthorizedAccessException)
            {
                return Unauthorized("Device authentication failed.");
            }
        }
    }
}