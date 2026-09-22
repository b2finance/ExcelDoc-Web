using System.Text;
using ExcelDoc.Server;
using ExcelDoc.Server.IoC;
using ExcelDoc.Server.Options;
using ExcelDoc.Server.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        "Seção de configuração 'Jwt' não configurada.");
var jwtValidation = new JwtOptionsValidator().Validate(null, jwtOptions);
if (jwtValidation.Failed)
{
    throw new InvalidOperationException(
        string.Join(Environment.NewLine, jwtValidation.Failures));
}

builder.Services.Configure<ProcessingOptions>(builder.Configuration.GetSection(ProcessingOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();

builder.Services
    .AddOptions<SapServiceLayerOptions>()
    .Bind(builder.Configuration.GetSection(SapServiceLayerOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<SapServiceLayerOptions>,
    SapServiceLayerOptionsValidator>();

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddInfrastructureLanguages();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtOptions.SecretKey))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        AuthRoles.Administrador,
        policy => policy.RequireRole(AuthRoles.Administrador));
    options.AddPolicy(
        AuthRoles.Usuario,
        policy => policy.RequireRole(AuthRoles.Usuario));
});

builder.Services.AddInfrastructureRepositories();
builder.Services.AddOptions<LicenseOptions>().Bind(builder.Configuration.GetSection(LicenseOptions.SectionName));

builder.Services.AddHttpClient(ExcelDoc.Server.Services.LicenseService.ClientName, (provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<LicenseOptions>>().Value;
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        throw new InvalidOperationException("Licensing: BaseUrl deve ser uma URL HTTPS.");

    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddSingleton<ExcelDoc.Server.Services.Interfaces.ILicenseService, ExcelDoc.Server.Services.LicenseService>();

builder.Services
    .AddControllers()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(SharedResource));
    });
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapStaticAssets();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapFallbackToFile("/index.html");

app.Run();
