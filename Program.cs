using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;
using UniflowApi.Common;
using UniflowApi.Data;
using UniflowApi.Middleware;
using UniflowApi.Services;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection(JwtSettings.SectionName));
var jwt = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()!;

builder.Services.Configure<EquitySettings>(builder.Configuration.GetSection(EquitySettings.SectionName));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection(SmsSettings.SectionName));
builder.Services.AddHttpClient<ISmsGateway, AfricasTalkingSmsGateway>();
builder.Services.AddSingleton<IReceiptPdfService, ReceiptPdfService>();


builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<StoredProcExecutor>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddScoped<ITokenService, TokenService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Cors Policy", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            ValidateLifetime = true,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey =
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwt.Secret)),

            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(
                        "water_token",
                        out string? token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireClaim("type", "admin"));
    options.AddPolicy("CustomerOnly", policy => policy.RequireClaim("type", "customer"));
    options.AddPolicy("EquityBillerOnly", policy => policy.RequireClaim("type", "equity_biller"));
    options.AddPolicy("SuperAdminOnly", policy => policy.RequireClaim("type", "superadmin"));
});

builder.Services.AddTransient<UniflowApi.Middleware.EquityIpWhitelistMiddleware>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "UniflowDB Water & Irrigation API", Version = "v1" });
});

var app = builder.Build();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "UniflowDB API");
        options.Interceptors.RequestInterceptorFunction = "function (req) { req.credentials = 'include'; return req; }";
    });

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("Cors Policy");
app.UseHttpsRedirection();

app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/api/v1/equity"),
    branch => branch.UseMiddleware<UniflowApi.Middleware.EquityIpWhitelistMiddleware>());

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check
app.MapGet("/api/v1/health", () => Results.Ok(new
{
    success = true,
    message = "UniflowDB Water & Irrigation Management API is running",
    timestamp = DateTime.UtcNow.ToString("o"),
    version = "1.0.0"
}));

app.Run();
