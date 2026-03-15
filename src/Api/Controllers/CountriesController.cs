using Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Api.Controllers;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[ApiController]
[Produces("application/json")]
public class CountriesController : ControllerBase
{
    private readonly IAppCountryService _appCountryService;

    public CountriesController(IAppCountryService appCountryService)
    {
        _appCountryService = appCountryService;
    }

    [OutputCache(PolicyName = "countries")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> GetAllCountriesAsync()
    {
        var countries = await _appCountryService.GetAllCountriesAsync();
        if (countries.IsSuccess)
        {
            return Ok(countries.Value);
        }
        else
        {
            return BadRequest(countries.Error);
        }
    }
}
