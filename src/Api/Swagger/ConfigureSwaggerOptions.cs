using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Api.Swagger;

/// <summary>
/// Dynamically generates a SwaggerDoc entry for each API version discovered by
/// Asp.Versioning, so Swagger UI shows a separate spec per version.
/// </summary>
public sealed class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;

    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
        => _provider = provider;

    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new OpenApiInfo
            {
                Title = "Auth Starter API",
                Version = description.ApiVersion.ToString(),
                Description = description.IsDeprecated
                    ? "Auth Starter API — this version is deprecated."
                    : "ASP.NET Core JWT + Passkey authentication starter template."
            });
        }
    }
}
