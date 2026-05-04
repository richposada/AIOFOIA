using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoiaProcessor.Api.Controllers;

/// <summary>
/// Anonymous endpoint that surfaces the Entra ID configuration the SPA needs
/// to initialize MSAL. Lets us ship a single static SPA bundle that adapts
/// to whichever environment serves it.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/config")]
public class ConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public ConfigController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("")]
    public IActionResult Get()
    {
        var instance = _config["AzureAd:Instance"]?.TrimEnd('/') ?? "https://login.microsoftonline.com";
        var tenantId = _config["AzureAd:TenantId"] ?? string.Empty;
        var clientId = _config["AzureAd:ClientId"] ?? string.Empty;
        var apiScope = string.IsNullOrEmpty(clientId)
            ? string.Empty
            : $"api://{clientId}/access_as_user";

        return Ok(new
        {
            authority = $"{instance}/{tenantId}",
            tenantId,
            clientId,
            apiScope,
        });
    }
}
