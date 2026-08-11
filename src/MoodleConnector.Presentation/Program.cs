using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MoodleConnector.Application;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.Configuration;
using MoodleConnector.Application.MoodleApi;
using MoodleConnector.Application.Gradebook.Queries;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Application.Registry;
using MoodleConnector.Application.Courses;
using MoodleConnector.Application.Activities;
using MoodleConnector.Application.Risk.Queries;
using MoodleConnector.Application.Participants;
using MoodleConnector.Application.Submissions.Queries;
using MoodleConnector.Domain;
using MoodleConnector.Infrastructure;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Security;
using MoodleConnector.Presentation.Tools;
using MoodleConnector.Presentation.Tools.Grading;
using MoodleConnector.Presentation.Tools.Gradebook;
using MoodleConnector.Presentation.Tools.Completion;
using MoodleConnector.Presentation.Tools.Risk;
using MoodleConnector.Presentation.Tools.Forums;
using MoodleConnector.Presentation.Tools.Submissions;
using MoodleConnector.Presentation.Tools.Messages;
using MoodleConnector.Presentation.Tools.Reports;
using MoodleConnector.Presentation.Tools.Monitor;
using MoodleConnector.Presentation.Tools.Memory;
using MoodleConnector.Presentation.Tools.Pedagogy;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Reflection;
using System.Threading.RateLimiting;
using MediatR;
using MoodleConnector.Application.Grading;
using MoodleConnector.Application.Messages;
using MoodleConnector.Application.Reports.Queries;
using MoodleConnector.Presentation;
using MoodleConnector.Presentation.Health;
using MoodleConnector.Infrastructure.Reports;

var builder = WebApplication.CreateBuilder(args);
const string AppAuthRateLimitPolicy = "app-auth";
const string AdminApiRateLimitPolicy = "admin-api";

// Windows adds EventLog as a default provider, which requires elevated
// permissions and makes integration tests fail before the host starts.
if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    var testKeyPath = Path.Combine(Path.GetTempPath(), "moodle-connector-tests", "keys");
    Directory.CreateDirectory(testKeyPath);
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(testKeyPath));
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Lax;
});
builder.Services.AddHealthChecks()
    .AddCheck<ConnectorDatabaseHealthCheck>("database", tags: ["ready"]);

builder.Services
    .AddOptions<McpServerSecurityOptions>()
    .Bind(builder.Configuration.GetSection(McpServerSecurityOptions.SectionName));

builder.Services
    .AddOptions<OAuthBrokerOptions>()
    .Bind(builder.Configuration.GetSection(OAuthBrokerOptions.SectionName));

builder.Services
    .AddOptions<UserClaimsOptions>()
    .Bind(builder.Configuration.GetSection(UserClaimsOptions.SectionName));

builder.Services
    .AddOptions<AdminApiOptions>()
    .Bind(builder.Configuration.GetSection(AdminApiOptions.SectionName));

builder.Services
    .AddOptions<FeatureOptions>()
    .Bind(builder.Configuration.GetSection(FeatureOptions.SectionName));

builder.Services
    .AddOptions<AssignmentWriteFeatureOptions>()
    .Bind(builder.Configuration.GetSection(AssignmentWriteFeatureOptions.SectionName));

builder.Services
    .AddOptions<MessageWriteFeatureOptions>()
    .Bind(builder.Configuration.GetSection(MessageWriteFeatureOptions.SectionName));

builder.Services
    .AddOptions<MoodleUniversalApiFeatureOptions>()
    .Bind(builder.Configuration.GetSection(MoodleUniversalApiFeatureOptions.SectionName));

builder.Services
    .AddOptions<GradingLimitsOptions>()
    .Bind(builder.Configuration.GetSection(GradingLimitsOptions.SectionName));

builder.Services
    .AddOptions<PendingActionOptions>()
    .Bind(builder.Configuration.GetSection(PendingActionOptions.SectionName));

builder.Services
    .AddOptions<ConnectorRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(ConnectorRateLimitOptions.SectionName));

builder.Services.AddSingleton<McpFixedWindowRateLimiter>();

// Register exposure policy via factory so configuration set by test hosts (WithWebHostBuilder)
// is respected when the DI container is built.
builder.Services.AddSingleton<IMcpToolExposurePolicy>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var exposureProfileString = cfg["MCP_EXPOSURE_PROFILE"] ?? "Production";
    if (Enum.TryParse<ToolExposureProfile>(exposureProfileString, true, out var exposureProfile))
    {
        return new CognitiveExposurePolicy(exposureProfile);
    }
    return new CognitiveExposurePolicy(ToolExposureProfile.Production);
});

// Build deterministic tool metadata registry once at startup and register a pre-populated instance.
// Avoid building a temporary ServiceProvider to prevent creating multiple DI containers
// and duplicated singleton instances.
var toolMetadataRegistry = new ToolMetadataRegistry(RegisteredMcpToolContainers.All);

builder.Services.AddSingleton(toolMetadataRegistry);
builder.Services.AddSingleton<ToolSurfaceInventory>();


var mcpSecurityOptions = builder.Configuration
    .GetSection(McpServerSecurityOptions.SectionName)
    .Get<McpServerSecurityOptions>() ?? new McpServerSecurityOptions();

builder.Services.AddAuthorization(options => options.AddMoodleScopePolicies());
var rateLimitOptions = builder.Configuration
    .GetSection(ConnectorRateLimitOptions.SectionName)
    .Get<ConnectorRateLimitOptions>() ?? new ConnectorRateLimitOptions();
var rateLimitWindow = TimeSpan.FromSeconds(Math.Clamp(rateLimitOptions.WindowSeconds, 1, 3600));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AppAuthRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimitOptions.AppAuthPermitLimit, 1, 1000),
                Window = rateLimitWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy(AdminApiRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimitOptions.AdminApiPermitLimit, 1, 1000),
                Window = rateLimitWindow,
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var oauthOptions = builder.Configuration
    .GetSection(OAuthBrokerOptions.SectionName)
    .Get<OAuthBrokerOptions>() ?? new OAuthBrokerOptions();
var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");
var appDomain = Environment.GetEnvironmentVariable("APP_DOMAIN") ??
                builder.Configuration["APP_DOMAIN"] ??
                string.Empty;
var publicBaseUrl = BuildPublicBaseUrlFromAppDomain(appDomain) ??
                    (isTestingEnvironment ? "http://localhost" : string.Empty);
var oauthIssuer = ResolveOAuthIssuer(oauthOptions, publicBaseUrl);
var oauthAudience = ResolveOAuthAudience(oauthOptions, publicBaseUrl, "/mcp");
var requireHttpsMetadata = oauthOptions.RequireHttpsMetadata && !isTestingEnvironment;
var oauthSigningCertificate = LoadOrCreateOAuthCertificate(
    oauthOptions,
    "signing",
    "Moodle Connector OAuth Signing",
    X509KeyUsageFlags.DigitalSignature);
var oauthEncryptionCertificate = LoadOrCreateOAuthCertificate(
    oauthOptions,
    "encryption",
    "Moodle Connector OAuth Encryption",
    X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment);

ValidateMcpAuthConfiguration(builder.Environment, mcpSecurityOptions, oauthIssuer, oauthAudience, oauthOptions);

var postgresOptionsForValidation = builder.Configuration.GetSection(PostgresOptions.SectionName).Get<PostgresOptions>();
var secretsOptionsForValidation = builder.Configuration.GetSection(ConnectorSecretsOptions.SectionName).Get<ConnectorSecretsOptions>();
var adminApiOptionsForValidation = builder.Configuration.GetSection(AdminApiOptions.SectionName).Get<AdminApiOptions>();
ValidateProductionSecuritySettings(builder.Environment, postgresOptionsForValidation, secretsOptionsForValidation, adminApiOptionsForValidation);

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "moodle-connector-app";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = requireHttpsMetadata
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.LoginPath = "/auth/login";
        options.LogoutPath = "/auth/logout";
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.Authority = oauthIssuer;
        options.Audience = oauthAudience;
        options.RequireHttpsMetadata = requireHttpsMetadata;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = oauthIssuer,
            ValidAudience = oauthAudience,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore()
            .UseDbContext<ConnectorDbContext>();
    })
    .AddServer(options =>
    {
        options.SetIssuer(new Uri(oauthIssuer));
        options.SetAuthorizationEndpointUris("/authorize");
        options.SetTokenEndpointUris("/token");
        options.SetConfigurationEndpointUris("/.well-known/openid-configuration");
        options.SetJsonWebKeySetEndpointUris("/.well-known/jwks");

        options.AllowAuthorizationCodeFlow()
            .AllowRefreshTokenFlow();

        options.RequireProofKeyForCodeExchange();
        options.DisableAccessTokenEncryption();
        options.SetAccessTokenLifetime(TimeSpan.FromMinutes(Math.Max(5, oauthOptions.AccessTokenMinutes)));
        options.SetRefreshTokenLifetime(TimeSpan.FromDays(Math.Max(1, oauthOptions.RefreshTokenDays)));
        options.RegisterResources(oauthAudience);
        options.RegisterScopes(GetMcpOauthScopes(oauthOptions));

        options.AddEncryptionCertificate(oauthEncryptionCertificate)
            .AddSigningCertificate(oauthSigningCertificate);

        var aspNetCore = options.UseAspNetCore()
            .EnableAuthorizationEndpointPassthrough();
        if (!requireHttpsMetadata)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    });

var mcpServerBuilder = builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMcpServer(options => options.ServerInstructions = MoodleConnectorInstructions.Text)
    .WithHttpTransport()
    .WithRequestFilters(filters =>
    {
        filters.AddCallToolFilter(next => async (request, cancellationToken) =>
        {
            try
            {
                var toolName = request.Params?.Name ?? string.Empty;
                var registry = request.Services?.GetService<ToolMetadataRegistry>();
                if (registry is null || !registry.TryGet(toolName, out var metadata) || metadata is null ||
                    string.IsNullOrWhiteSpace(metadata.RequiredPlatformPermission))
                {
                    return ToolResultHelper.Error<object>(
                        "Esta tool nÃ£o possui uma permissÃ£o de plataforma configurada e foi bloqueada.",
                        errorCode: "platform_permission_not_configured");
                }

                var httpContext = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
                if (!HasPlatformToolPermission(httpContext?.User, metadata.RequiredPlatformPermission))
                {
                    return ToolResultHelper.Error<object>(
                        $"O usuÃ¡rio nÃ£o possui a permissÃ£o de plataforma '{metadata.RequiredPlatformPermission}'.",
                        errorCode: "platform_permission_denied");
                }

                return await next(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var descriptor = MoodleErrorContract.Describe(exception);
                var loggerFactory = request.Services?.GetService<ILoggerFactory>();
                loggerFactory?
                    .CreateLogger("MoodleConnector.McpToolBoundary")
                    .LogError(
                        exception,
                        "Unhandled MCP tool exception was converted to a structured result. AuditId={AuditId} ErrorCode={ErrorCode}",
                        descriptor.AuditId,
                        descriptor.ErrorCode);
                return ToolResultHelper.Error<object>(exception);
            }
        });

        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            if (request.Services is null)
            {
                return result;
            }

            var security = request.Services.GetRequiredService<IOptions<McpServerSecurityOptions>>().Value;

            // Apply exposure policy BEFORE serialization/transport so JSON vs SSE is irrelevant.
            var policy = request.Services.GetService<IMcpToolExposurePolicy>();
            if (policy != null)
            {
                var registry = request.Services.GetService<ToolMetadataRegistry>();

                // Remove tools that the policy says should not be exposed.
                // Iterate in reverse to allow removal from the collection.
                for (int i = result.Tools.Count - 1; i >= 0; i--)
                {
                    var tool = result.Tools[i];
                    if (tool == null) continue;

                    MoodleToolMetadataAttribute? metadata = null;
                    if (registry != null)
                    {
                        registry.TryGet(tool.Name, out metadata);
                    }

                    var expose = policy.ShouldExpose(tool.Name ?? string.Empty, metadata);

                    if (!expose)
                    {
                        result.Tools.RemoveAt(i);
                    }
                }
            }

            // Post-process remaining tools for metadata and security schemes
            foreach (var tool in result.Tools)
            {
                AddGradingReviewToolMetadata(tool);

                if (security.RequireJwt)
                {
                    AddOAuthSecuritySchemes(tool);
                }
            }

            return result;
        });
    });

// The same explicit catalog drives MCP registration and metadata registration.
mcpServerBuilder
    .WithTools((IEnumerable<Type>)RegisteredMcpToolContainers.AlwaysOn, JsonSerializerOptions.Default)
    .WithResources<MoodleGradingReviewAppResources>();

// ToolMetadataRegistry was pre-populated and registered above; do not build temporary providers here.

var featureOptions = builder.Configuration.GetSection(FeatureOptions.SectionName).Get<FeatureOptions>() ?? new FeatureOptions();
var assignmentWriteOptions = builder.Configuration
    .GetSection(AssignmentWriteFeatureOptions.SectionName)
    .Get<AssignmentWriteFeatureOptions>() ?? new AssignmentWriteFeatureOptions();
var enabledConditionalToolContainers = RegisteredMcpToolContainers.GetEnabledContainers(
    featureOptions,
    assignmentWriteOptions);
if (enabledConditionalToolContainers.Count > 0)
{
    mcpServerBuilder.WithTools(
        (IEnumerable<Type>)enabledConditionalToolContainers,
        JsonSerializerOptions.Default);
}

var app = builder.Build();
var appV2Enabled = builder.Configuration.GetValue<bool>("Features:AppV2Enabled");

// ToolMetadataRegistry is registered and pre-populated at startup.

app.UseForwardedHeaders();

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].ToString();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 100)
    {
        correlationId = Guid.NewGuid().ToString();
    }
    context.TraceIdentifier = correlationId;

    if (context.Request.Path.StartsWithSegments("", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers["X-Correlation-ID"] = correlationId;
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; img-src 'self' data: https:; font-src 'self' https://fonts.gstatic.com; connect-src 'self'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            return Task.CompletedTask;
        });
    }
    await next();
});
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (AntiforgeryValidationException) when (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new
        {
            error = new
            {
                code = "csrf_invalid",
                message = "Token CSRF ausente, invÃ¡lido ou expirado. Atualize a pÃ¡gina e tente novamente."
            }
        });
    }
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

using (var scope = app.Services.CreateScope())
{
    if (!app.Environment.IsEnvironment("Testing"))
    {
        var db = scope.ServiceProvider.GetRequiredService<ConnectorDbContext>();
        await db.ApplyVersionedSchemaAsync();
        await SeedChatGptOAuthClientAsync(scope.ServiceProvider, app.Logger, appDomain, app.Environment);
    }
}

const string mcpPath = "/mcp";

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments(mcpPath, StringComparison.OrdinalIgnoreCase) ||
        HttpMethods.IsOptions(context.Request.Method))
    {
        await next();
        return;
    }

    var securityOptions = context.RequestServices.GetRequiredService<IOptions<McpServerSecurityOptions>>().Value;
    if (!HasMcpCredentials(context, securityOptions) &&
        await IsMcpDiscoveryRequestAsync(context))
    {
        await next();
        return;
    }

    var hasApiKey = !string.IsNullOrWhiteSpace(context.Request.Headers[securityOptions.ApiKeyHeader].ToString());
    var hasBearerToken = context.Request.Headers.Authorization.ToString()
        .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    var isAuthenticated = false;

    if (securityOptions.RequireApiKey && hasApiKey)
    {
        var providedApiKey = context.Request.Headers[securityOptions.ApiKeyHeader].ToString();
        var resolver = context.RequestServices.GetRequiredService<IMcpConnectorClientResolver>();
        var client = await resolver.ResolveByApiKeyAsync(providedApiKey, context.RequestAborted);
        if (client is not null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, client.ClientId),
                new("connector_client_id", client.ClientId)
            };

            if (client.CanWrite)
            {
                claims.Add(new("scope", "moodle.write"));
            }

            // API keys represent connector clients, not app users. Keep their
            // explicit legacy read/write contract visible to the same central
            // platform-permission gate used by JWT and app identities.
            foreach (var permission in PlatformPermissionCatalog.AllRead)
            {
                claims.Add(new Claim("platform_permission", permission));
            }
            if (client.CanWrite)
            {
                foreach (var permission in PlatformPermissionCatalog.AllWrite)
                {
                    claims.Add(new Claim("platform_permission", permission));
                }
            }

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "connector-api-key"));
            isAuthenticated = true;
        }
        else if (!securityOptions.RequireJwt || !hasBearerToken)
        {
            if (securityOptions.RequireJwt &&
                await TryWriteMcpOauthToolChallengeAsync(
                    context,
                    "invalid_token",
                    "API key invalida. FaÃ§a login via OAuth para continuar."))
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await AuditMcpAuthorizationFailureAsync(context, "invalid_api_key", "API key do conector invalida ou inativa.");
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                error = "invalid_api_key",
                message = "API key do conector invalida ou inativa."
            });
            return;
        }
    }

    if (!isAuthenticated && securityOptions.RequireJwt && hasBearerToken)
    {
        var authResult = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
        if (!authResult.Succeeded || authResult.Principal?.Identity?.IsAuthenticated != true)
        {
            if (await TryWriteMcpOauthToolChallengeAsync(
                    context,
                    "invalid_token",
                    "JWT ausente, expirado ou rejeitado pelo broker OAuth. FaÃ§a login novamente."))
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            SetMcpOauthAuthenticateHeader(context);
            await AuditMcpAuthorizationFailureAsync(context, "missing_or_invalid_jwt", "JWT ausente, invalido ou rejeitado pelo broker OAuth.");
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                error = "missing_or_invalid_jwt",
                message = "Envie Authorization: Bearer <jwt> valido emitido pelo broker OAuth."
            });
            return;
        }

        context.User = authResult.Principal;
        await EnrichMcpPrincipalFromLocalAccountAsync(context, context.RequestAborted);
        if (!context.User.HasClaim(claim => claim.Type == "connector_client_id"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await AuditMcpAuthorizationFailureAsync(
                context,
                "moodle_connection_not_linked",
                "Usuario autenticado sem conexao Moodle vinculada.");
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                error = "moodle_connection_not_linked",
                message = "Conecte uma conta Moodle antes de usar as tools do conector."
            });
            return;
        }

        isAuthenticated = true;
    }

    if (!isAuthenticated)
    {
        if (securityOptions.RequireJwt && securityOptions.RequireApiKey)
        {
            if (await TryWriteMcpOauthToolChallengeAsync(
                    context,
                    "invalid_token",
                    "AutenticaÃ§Ã£o OAuth necessÃ¡ria para usar as tools do Moodle Connector."))
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            SetMcpOauthAuthenticateHeader(context);
            await AuditMcpAuthorizationFailureAsync(context, "missing_mcp_credentials", "Credenciais MCP ausentes.");
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                error = "missing_mcp_credentials",
                message = $"Envie Authorization: Bearer <jwt> valido ou o header {securityOptions.ApiKeyHeader} com uma API key valida do conector."
            });
            return;
        }

        if (securityOptions.RequireJwt)
        {
            if (await TryWriteMcpOauthToolChallengeAsync(
                    context,
                    "invalid_token",
                    "AutenticaÃ§Ã£o OAuth necessÃ¡ria para usar as tools do Moodle Connector."))
            {
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            SetMcpOauthAuthenticateHeader(context);
            await AuditMcpAuthorizationFailureAsync(context, "missing_or_invalid_jwt", "JWT ausente ou invalido.");
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                error = "missing_or_invalid_jwt",
                message = "Envie Authorization: Bearer <jwt> valido emitido pelo broker OAuth."
            });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await AuditMcpAuthorizationFailureAsync(context, "missing_api_key", "API key MCP ausente.");
        await context.Response.WriteAsJsonAsync(new
        {
            ok = false,
            error = "missing_api_key",
            message = $"Envie o header {securityOptions.ApiKeyHeader} com uma API key valida do conector."
        });
        return;
    }

    var rateLimiter = context.RequestServices.GetRequiredService<McpFixedWindowRateLimiter>();
    var rateLimitPartitionKey = GetMcpRateLimitPartitionKey(context, securityOptions.ApiKeyHeader);
    if (!rateLimiter.TryAcquire(rateLimitPartitionKey, out var retryAfter))
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
        await context.Response.WriteAsJsonAsync(new
        {
            ok = false,
            error = "rate_limited",
            message = "Limite de chamadas MCP excedido para este usuario/conector. Tente novamente depois."
        });
        return;
    }

    try
    {
        await next();
    }
    catch (JsonException ex) when (context.Request.Path.StartsWithSegments(mcpPath, StringComparison.OrdinalIgnoreCase))
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await WriteMcpRequestParseErrorAsync(
            context,
            app.Logger,
            ex,
            "invalid_mcp_request",
            "RequisiÃ§Ã£o MCP invalida. Envie um payload JSON-RPC 2.0 valido.");
    }
    catch (BadHttpRequestException ex) when (context.Request.Path.StartsWithSegments(mcpPath, StringComparison.OrdinalIgnoreCase))
    {
        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await WriteMcpRequestParseErrorAsync(
            context,
            app.Logger,
            ex,
            "invalid_mcp_request",
            "RequisiÃ§Ã£o MCP invalida. Nao foi possivel ler o payload recebido.");
    }
});

app.UseRateLimiter();

app.MapGet("/api/status", (
    HttpContext context,
    IOptions<McpServerSecurityOptions> security,
    IOptions<OAuthBrokerOptions> oauth,
    IOptions<AssignmentWriteFeatureOptions> assignmentWrites,
    IOptions<FeatureOptions> features,
    ToolSurfaceInventory inventory) =>
{
    var publicBaseUrl = GetPublicBaseUrl(context);
    var gitCommit = builder.Configuration["GIT_COMMIT"] ?? "unknown";
    var buildDate = builder.Configuration["BUILD_DATE"] ?? "unknown";
    var demoToolCount = inventory.Entries.Count(entry =>
        entry.Family.Equals("demopendingaction", StringComparison.OrdinalIgnoreCase));
    var individualGradeToolCount = inventory.Entries.Count(entry =>
        entry.Family.Contains("individualgrade", StringComparison.OrdinalIgnoreCase));
    var toolsCount = inventory.Total -
                     (features.Value.DemoToolsEnabled ? 0 : demoToolCount) -
                     (assignmentWrites.Value.AssignmentGradeWriteEnabled ? 0 : individualGradeToolCount);
    return Results.Ok(new
    {
        ok = true,
        service = "moodle-gpt-connector",
        status = "online",
        transport = "mcp-streamable-http",
        endpoint = mcpPath,
        source = "aspnetcore-mcp",
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
        gitCommit,
        buildDate,
        toolsCount,
        universalExecutorEnabled = true,
        capabilityDiscoveryEnabled = true,
        auth = new
        {
            requireJwt = security.Value.RequireJwt,
            requireApiKey = security.Value.RequireApiKey,
            issuer = ResolveOAuthIssuer(oauth.Value, publicBaseUrl),
            audience = ResolveOAuthAudience(oauth.Value, publicBaseUrl, mcpPath),
            chatGptClientConfigured = !string.IsNullOrWhiteSpace(oauth.Value.ChatGptClientId),
            chatGptRedirectConfigured = !string.IsNullOrWhiteSpace(oauth.Value.ChatGptRedirectUri)
        }
    });
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapGet("/.well-known/oauth-protected-resource", BuildOAuthProtectedResourceMetadata);
app.MapGet("/.well-known/oauth-protected-resource/{**resourcePath}", BuildOAuthProtectedResourceMetadata);
app.MapGet("/.well-known/oauth-authorization-server", BuildOAuthAuthorizationServerMetadata);

app.MapMethods("/authorize", new[] { HttpMethods.Get, HttpMethods.Post }, async (
    HttpContext context,
    ConnectorDbContext dbContext,
    IOptions<OAuthBrokerOptions> oauth,
    CancellationToken cancellationToken) =>
{
    var request = Microsoft.AspNetCore.OpenIddictServerAspNetCoreHelpers.GetOpenIddictServerRequest(context)
        ?? throw new InvalidOperationException("Requisicao OAuth invalida.");

    var authenticateResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (authenticateResult.Principal?.Identity?.IsAuthenticated != true)
    {
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        return Results.Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var returnUrl = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        return Results.Redirect($"/auth/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }

    var claims = new ClaimsIdentity(
        TokenValidationParameters.DefaultAuthenticationType,
        OpenIddictConstants.Claims.Name,
        OpenIddictConstants.Claims.Role);

    claims.SetClaim(OpenIddictConstants.Claims.Subject, identity.Id.ToString());
    claims.SetClaim(OpenIddictConstants.Claims.Name, identity.Name);
    claims.SetClaim(OpenIddictConstants.Claims.Email, identity.Email);
    claims.SetClaim(
        "connector_client_id",
        string.IsNullOrWhiteSpace(identity.ConnectorClientId)
            ? identity.Id.ToString()
            : identity.ConnectorClientId);

    var principal = new ClaimsPrincipal(claims);
    principal.SetScopes(request.GetScopes());

    var resource = request.GetParameter("resource")?.ToString();
    if (string.IsNullOrWhiteSpace(resource))
    {
        resource = ResolveOAuthAudience(oauth.Value, GetPublicBaseUrl(context), mcpPath);
    }
    principal.SetResources(resource);

    foreach (var claim in principal.Claims)
    {
        claim.SetDestinations(GetOAuthClaimDestinations(claim));
    }

    return Results.SignIn(
        principal,
        authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
});



app.MapGet("/api/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { token = tokens.RequestToken });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/tasks", async (HttpContext context, ConnectorDbContext dbContext, int page = 1, int pageSize = 20, string? status = null, string? priority = null, CancellationToken cancellationToken = default) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
    var query = dbContext.Tasks.AsNoTracking().Where(x => x.OwnerId == identity.Id);
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
    if (!string.IsNullOrWhiteSpace(priority)) query = query.Where(x => x.Priority == priority);
    var total = await query.CountAsync(cancellationToken);
    var items = await query.OrderBy(x => x.DueAt).ThenByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new TaskDto(x.Id, x.Title, x.Description, x.Status, x.Priority, x.DueAt, x.CreatedAt, x.UpdatedAt)).ToListAsync(cancellationToken);
    return Results.Ok(new AppListEnvelope<TaskDto>(items, new AppListMeta(page, pageSize, items.Count, page * pageSize < total, DateTimeOffset.UtcNow, null, null, total)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/agenda", async (HttpContext context, ConnectorDbContext dbContext, DateTimeOffset? from = null, DateTimeOffset? to = null, CancellationToken cancellationToken = default) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var start = from ?? DateTimeOffset.UtcNow.Date;
    var end = to ?? start.AddDays(30);
    var events = await dbContext.CalendarEvents.AsNoTracking().Where(x => x.OwnerId == identity.Id && x.StartAt >= start && x.StartAt < end).OrderBy(x => x.StartAt).Select(x => new CalendarEventDto(x.Id, x.Title, x.Description, x.StartAt, x.EndAt, x.Type, x.CreatedAt, x.UpdatedAt)).ToListAsync(cancellationToken);
    return Results.Ok(new AppEnvelope<IReadOnlyList<CalendarEventDto>>(events, new(DateTimeOffset.UtcNow, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/followups", async (HttpContext context, ConnectorDbContext dbContext, string? studentRef = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
    var query = dbContext.Followups.AsNoTracking().Where(x => x.OwnerId == identity.Id);
    if (!string.IsNullOrWhiteSpace(studentRef)) query = query.Where(x => x.StudentRef == studentRef);
    var total = await query.CountAsync(cancellationToken);
    var items = await query.OrderByDescending(x => x.OccurredAt).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new FollowupDto(x.Id, x.StudentRef, x.CourseRef, x.Kind, x.Notes, x.OccurredAt, x.CreatedAt)).ToListAsync(cancellationToken);
    return Results.Ok(new AppListEnvelope<FollowupDto>(items, new(page, pageSize, items.Count, page * pageSize < total, DateTimeOffset.UtcNow, null, null, total)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/reports/operational", async (HttpContext context, ConnectorDbContext dbContext, CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var now = DateTimeOffset.UtcNow;
    var openTasks = await dbContext.Tasks.CountAsync(x => x.OwnerId == identity.Id && x.Status != "done", cancellationToken);
    var completedTasks = await dbContext.Tasks.CountAsync(x => x.OwnerId == identity.Id && x.Status == "done", cancellationToken);
    var upcomingEvents = await dbContext.CalendarEvents.CountAsync(x => x.OwnerId == identity.Id && x.StartAt >= now && x.StartAt < now.AddDays(30), cancellationToken);
    var followups = await dbContext.Followups.CountAsync(x => x.OwnerId == identity.Id, cancellationToken);
    return Results.Ok(new AppEnvelope<AppOperationalReportDto>(new(openTasks, completedTasks, upcomingEvents, followups, now), new(now, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/reports/audit", async (
    HttpContext context,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ReportsView)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var generatedAt = DateTimeOffset.UtcNow;
    var actor = identity.Id.ToString();
    var query = dbContext.MoodleAuditLogs.AsNoTracking().Where(log => log.ActorSubject == actor);
    var total = await query.CountAsync(cancellationToken);
    var completed = await query.CountAsync(log => log.Status == "success" || log.Status == "completed", cancellationToken);
    var failed = await query.CountAsync(log => log.Status == "failed" || log.Status == "error", cancellationToken);
    var confirmed = await query.CountAsync(log => log.PendingActionId != null, cancellationToken);
    return Results.Ok(new AppEnvelope<AppAuditReportDto>(
        new(total, completed, failed, confirmed, generatedAt), new(generatedAt, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/reports/course-overview/{connectionRef}/{courseId}", async (
    string connectionRef,
    string courseId,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ReportsView)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken) is null)
        return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var report = await mediator.Send(new GenerateCourseOverviewQuery(courseId), cancellationToken);
    var now = DateTimeOffset.UtcNow;
    return Results.Ok(new AppEnvelope<AppCourseOverviewReportDto>(
        new(connectionRef, report.CourseId, report.GeneratedAt, report.TotalActiveStudents, report.StudentsWhoAccessed,
            report.StudentsNeverAccessed, report.StudentsInactiveDays, report.InactiveDaysThreshold,
            report.TotalGradedItems, report.AverageBelowMinimumPerStudent, report.SuggestedActionsForTutor, report.Warning),
        new(report.GeneratedAt, connectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/reports/weekly/{connectionRef}/{courseId}", async (
    string connectionRef,
    string courseId,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ReportsView)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken) is null)
        return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var report = await mediator.Send(new GenerateWeeklyPerformanceReportQuery(courseId, MaxStudentsToAnalyze: 60), cancellationToken);
    return Results.Ok(new AppEnvelope<AppWeeklyReportDto>(
        new(connectionRef, report.CourseId, report.GeneratedAt, report.TotalStudents, report.StudentsWithAttention,
            report.StudentsAtRisk, report.MinGradePercent, report.InactiveDaysThreshold, report.Warning),
        new(report.GeneratedAt, connectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/reports/completion/{connectionRef}/{courseId}", async (
    string connectionRef,
    string courseId,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ReportsView)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken) is null)
        return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var report = await mediator.Send(new GeneratePostExecutionReportQuery(courseId, MaxStudentsToAnalyze: 60), cancellationToken);
    return Results.Ok(new AppEnvelope<AppCompletionReportDto>(
        new(connectionRef, report.CourseId, report.GeneratedAt, report.TotalStudents, report.LikelyComplete,
            report.PendingRecovery, report.AtRisk, report.Unknown, report.MinGradePercent, report.Disclaimer, report.Warning),
        new(report.GeneratedAt, connectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/messages/prepare", async (HttpContext context, ConnectorDbContext dbContext, IMediator mediator, AppMessagePrepareInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.MessagesPrepare)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (!Enum.TryParse<TutorMessageType>(input.MessageType, true, out var messageType)) return Results.BadRequest(new { error = new { code = "invalid_message_type", message = "Tipo de mensagem invÃ¡lido." } });
    if (input.RecipientIds is null || input.RecipientIds.Count == 0 || input.RecipientIds.Count > 100) return Results.BadRequest(new { error = new { code = "invalid_recipients", message = "Informe de 1 a 100 destinatÃ¡rios explÃ­citos." } });
    try
    {
        var preview = await mediator.Send(new PrepareTutorMessageCommand(input.CourseId, messageType, input.RecipientIds, input.CustomText), cancellationToken);
        return Results.Ok(new AppEnvelope<TutorMessagePreview>(preview, new(DateTimeOffset.UtcNow, null)));
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { error = new { code = "invalid_message", message = ex.Message } }); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { error = new { code = "message_disabled", message = ex.Message } }); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/messages/confirm", async (HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, IMediator mediator, AppMessageConfirmInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.MessagesPrepare)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    if (input.PendingActionId == Guid.Empty || string.IsNullOrWhiteSpace(input.ConfirmationText)) return Results.BadRequest(new { error = new { code = "invalid_confirmation", message = "ConfirmaÃ§Ã£o explÃ­cita Ã© obrigatÃ³ria." } });
    var result = await mediator.Send(new ConfirmTutorMessageCommand(input.PendingActionId, input.ConfirmationText), cancellationToken);
    return Results.Ok(new AppEnvelope<TutorMessageSendResult>(result, new(DateTimeOffset.UtcNow, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/followups", async (HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, FollowupInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.StudentsFollowupWrite)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    if (string.IsNullOrWhiteSpace(input.StudentRef) || string.IsNullOrWhiteSpace(input.Notes)) return Results.BadRequest(new { error = new { code = "invalid_followup", message = "Aluno e registro sÃ£o obrigatÃ³rios." } });
    var now = DateTimeOffset.UtcNow;
    var item = new FollowupEntity { Id = Guid.NewGuid(), OwnerId = identity.Id, StudentRef = input.StudentRef.Trim(), CourseRef = input.CourseRef?.Trim(), Kind = NormalizeFollowupKind(input.Kind), Notes = input.Notes.Trim(), OccurredAt = input.OccurredAt ?? now, CreatedAt = now };
    dbContext.Followups.Add(item); await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/followups/{item.Id}", new AppEnvelope<FollowupDto>(new(item.Id, item.StudentRef, item.CourseRef, item.Kind, item.Notes, item.OccurredAt, item.CreatedAt), new(now, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/agenda", async (HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, CalendarEventInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.AgendaManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = new { code = "invalid_title", message = "TÃ­tulo Ã© obrigatÃ³rio." } });
    var now = DateTimeOffset.UtcNow;
    var item = new CalendarEventEntity { Id = Guid.NewGuid(), OwnerId = identity.Id, Title = input.Title.Trim(), Description = input.Description?.Trim(), StartAt = input.StartAt, EndAt = input.EndAt, Type = NormalizeCalendarEventType(input.Type), CreatedAt = now, UpdatedAt = now };
    dbContext.CalendarEvents.Add(item); await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/agenda/{item.Id}", new AppEnvelope<CalendarEventDto>(new(item.Id, item.Title, item.Description, item.StartAt, item.EndAt, item.Type, item.CreatedAt, item.UpdatedAt), new(now, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapDelete("/api/agenda/{id:guid}", async (Guid id, HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.AgendaManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    var item = await dbContext.CalendarEvents.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == identity.Id, cancellationToken);
    if (item is null) return Results.NotFound();
    dbContext.CalendarEvents.Remove(item); await dbContext.SaveChangesAsync(cancellationToken); return Results.NoContent();
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/tasks", async (HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, TaskInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.TasksManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    if (string.IsNullOrWhiteSpace(input.Title)) return Results.BadRequest(new { error = new { code = "invalid_title", message = "TÃ­tulo Ã© obrigatÃ³rio." } });
    var now = DateTimeOffset.UtcNow;
    var task = new TaskEntity { Id = Guid.NewGuid(), OwnerId = identity.Id, Title = input.Title.Trim(), Description = input.Description?.Trim(), Status = NormalizeTaskStatus(input.Status), Priority = NormalizeTaskPriority(input.Priority), DueAt = input.DueAt, CreatedAt = now, UpdatedAt = now };
    dbContext.Tasks.Add(task); await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Created($"/api/tasks/{task.Id}", new AppEnvelope<TaskDto>(new(task.Id, task.Title, task.Description, task.Status, task.Priority, task.DueAt, task.CreatedAt, task.UpdatedAt), new(now, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPatch("/api/tasks/{id:guid}", async (Guid id, HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, TaskInput input, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.TasksManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    var task = await dbContext.Tasks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == identity.Id, cancellationToken);
    if (task is null) return Results.NotFound();
    if (!string.IsNullOrWhiteSpace(input.Title)) task.Title = input.Title.Trim();
    if (input.Description is not null) task.Description = input.Description.Trim();
    if (input.Status is not null) task.Status = NormalizeTaskStatus(input.Status);
    if (input.Priority is not null) task.Priority = NormalizeTaskPriority(input.Priority);
    if (input.DueAt is not null) task.DueAt = input.DueAt;
    task.UpdatedAt = DateTimeOffset.UtcNow; await dbContext.SaveChangesAsync(cancellationToken);
    return Results.Ok(new AppEnvelope<TaskDto>(new(task.Id, task.Title, task.Description, task.Status, task.Priority, task.DueAt, task.CreatedAt, task.UpdatedAt), new(task.UpdatedAt, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapDelete("/api/tasks/{id:guid}", async (Guid id, HttpContext context, ConnectorDbContext dbContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.TasksManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    var task = await dbContext.Tasks.SingleOrDefaultAsync(x => x.Id == id && x.OwnerId == identity.Id, cancellationToken);
    if (task is null) return Results.NotFound();
    dbContext.Tasks.Remove(task); await dbContext.SaveChangesAsync(cancellationToken); return Results.NoContent();
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/session", async (
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null)
    {
        return Results.Json(new AppEnvelope<AppSessionDto>(
            new(false, null), new AppMeta(DateTimeOffset.UtcNow, null)), statusCode: StatusCodes.Status401Unauthorized);
    }

    var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
    if (profile is null) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    var roles = context.User.FindAll(ClaimTypes.Role).Select(x => x.Value)
        .Concat(context.User.FindAll("role").Select(x => x.Value))
        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (roles.Length == 0) roles = ["Tutor"];
    var permissions = context.User.FindAll("platform_permission").Select(x => x.Value)
        .Where(permission => !context.User.FindAll("platform_permission_deny").Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return Results.Ok(new AppEnvelope<AppSessionDto>(
        new(true, new AppUserDto(profile.Id, profile.Name, roles, permissions)),
        new(DateTimeOffset.UtcNow, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/connections", async (
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
    if (profile is null) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    var connections = profile.MoodleConnections.Select(connection => new AppConnectionDto(
        connection.Id, connection.Alias, connection.Alias, connection.BaseUrl, connection.Status, connection.IsDefault,
        new[] { "read" }.Concat(connection.CanWrite ? new[] { "write" } : Array.Empty<string>()).ToArray(), connection.LastValidatedAt));
    return Results.Ok(new AppListEnvelope<AppConnectionDto>(
        connections.ToArray(), new(1, 20, connections.Count(), false, DateTimeOffset.UtcNow, null, null, connections.Count())));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/connections", async (
    ConnectMoodleInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ConnectionsManage)) return Results.Forbid();
    await antiforgery.ValidateRequestAsync(context);

    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(input.MoodleAlias) ||
        string.IsNullOrWhiteSpace(input.MoodleBaseUrl) ||
        string.IsNullOrWhiteSpace(input.MoodleUsername) ||
        string.IsNullOrWhiteSpace(input.MoodlePassword))
        return Results.BadRequest(new { ok = false, error = "Preencha alias, URL, usuario e senha do Moodle." });

    try
    {
        await accountService.ConnectMoodleAsync(
            new ConnectMoodleAccountRequest(
                identity.Id,
                input.MoodleAlias,
                input.MoodleBaseUrl,
                input.MoodleUsername,
                input.MoodlePassword,
                input.IsDefault,
                input.CanWrite),
            cancellationToken);

        var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
        var connection = profile?.MoodleConnections
            .FirstOrDefault(item => string.Equals(item.Alias, input.MoodleAlias.Trim(), StringComparison.OrdinalIgnoreCase));
        if (connection is null)
        {
            return Results.Problem(
                "A conexÃ£o foi registrada, mas nÃ£o pÃ´de ser relida com seguranÃ§a.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(new AppConnectionDto(
            connection.Id,
            connection.Alias,
            connection.Alias,
            connection.BaseUrl,
            connection.Status,
            connection.IsDefault,
            new[] { "read" }.Concat(connection.CanWrite ? new[] { "write" } : Array.Empty<string>()).ToArray(),
            connection.LastValidatedAt));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPut("/api/connections/{id}", async (
    string id,
    UpdateMoodleInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ConnectionsManage)) return Results.Forbid();
    await antiforgery.ValidateRequestAsync(context);
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.MoodleAlias) || string.IsNullOrWhiteSpace(input.MoodleBaseUrl))
        return Results.BadRequest(new { ok = false, error = "Preencha alias e URL do Moodle." });
    try
    {
        await accountService.UpdateMoodleAsync(new UpdateMoodleAccountRequest(identity.Id, id, input.MoodleAlias, input.MoodleBaseUrl, input.MoodleUsername, input.MoodlePassword, input.IsDefault, input.CanWrite), cancellationToken);
        var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
        var connection = profile?.MoodleConnections.FirstOrDefault(item => item.Id == id);
        return connection is null ? Results.NotFound() : Results.Ok(new AppConnectionDto(connection.Id, connection.Alias, connection.Alias, connection.BaseUrl, connection.Status, connection.IsDefault, new[] { "read" }.Concat(connection.CanWrite ? new[] { "write" } : Array.Empty<string>()).ToArray(), connection.LastValidatedAt));
    }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { ok = false, error = ex.Message }); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/connections/{id}/data-summary", async (string id, HttpContext context, IAccountService accountService, ConnectorDbContext dbContext, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ConnectionsManage)) return Results.Forbid();
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    try { return Results.Ok(await accountService.GetMoodleDataSummaryAsync(identity.Id, id, cancellationToken)); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { ok = false, error = ex.Message }); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapDelete("/api/connections/{id}", async (string id, [FromBody] AppDeleteConnectionInput input, HttpContext context, IAccountService accountService, ConnectorDbContext dbContext, IAntiforgery antiforgery, CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ConnectionsManage)) return Results.Forbid();
    await antiforgery.ValidateRequestAsync(context);
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    try { await accountService.DeleteMoodleAsync(identity.Id, id, input.DeleteLinkedData, input.ConfirmationText, cancellationToken); return Results.Ok(new { ok = true }); }
    catch (InvalidOperationException ex) { return Results.BadRequest(new { ok = false, error = ex.Message }); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/connections/{id}/validate", async (
    string id,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    if (!HasAppPermission(context, AppPermissionCatalog.ConnectionsManage)) return Results.Forbid();
    await antiforgery.ValidateRequestAsync(context);
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        var validation = await accountService.ValidateMoodleAsync(identity.Id, id, cancellationToken);
        return Results.Ok(new { status = validation.Status, lastValidatedAt = validation.LastValidatedAt });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/pending", async (
    string? connectionRef,
    string? courseId,
    string? type,
    string? level,
    string? studentId,
    int? periodDays,
    int? page,
    int? pageSize,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    var currentPage = Math.Max(page ?? 1, 1);
    var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var generatedAt = DateTimeOffset.UtcNow;
    MoodleConnector.Domain.Registry.ConnectionInfo? resolved;
    try
    {
        resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    }
    catch (MoodleApiException exception) when (exception.ErrorCode == "moodle_connection_not_found")
    {
        return Results.Ok(new AppListEnvelope<AppPendingDto>(
            Array.Empty<AppPendingDto>(), new(currentPage, size, 0, false, generatedAt, null,
                ["Nenhuma conexÃ£o Moodle foi configurada para esta conta."])));
    }
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var effectiveConnectionRef = connectionRef ?? resolved.Alias;
    if (string.IsNullOrWhiteSpace(courseId))
    {
        return Results.Ok(new AppListEnvelope<AppPendingDto>(
            Array.Empty<AppPendingDto>(), new(currentPage, size, 0, false, generatedAt, effectiveConnectionRef,
                ["Selecione um curso para consultar pendÃªncias; nenhuma consulta agregada foi executada."])));
    }

    var userId = identity.Id.ToString();
    var participants = await mediator.Send(new ListCourseParticipantsQuery(
        userId, courseId, ParticipantStatusFilter.Active, 1, 100, true, false), cancellationToken);
    if (participants is null) return AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.");

    var pending = await mediator.Send(new GetStudentsWithPendingSubmissionsQuery(courseId, 0, 100), cancellationToken);
    var inactivityDays = Math.Clamp(periodDays ?? 14, 1, 3650);
    var cutoff = generatedAt.AddDays(-inactivityDays);
    var accessRows = participants.Participants
        .Where(student => string.IsNullOrWhiteSpace(studentId) || student.UserId == studentId)
        .Where(student => student.LastCourseAccessAt is null || student.LastCourseAccessAt < cutoff)
        .Select(student => new AppPendingAccessRow(student.UserId, student.FullName, student.LastCourseAccessAt));
    var submissionRows = pending.Students
        .Where(student => string.IsNullOrWhiteSpace(studentId) || student.StudentId == studentId)
        .SelectMany(student => student.PendingAssignments.Select(activity => new AppPendingSourceRow(
            student.StudentId, student.FullName, student.LastCourseAccessAt,
            activity.AssignmentId, activity.AssignmentName, "pending_submission", activity.DueDate,
            activity.IsOverdue, false)));

    var allItems = AppPendingContractMapper.Build(effectiveConnectionRef, courseId, submissionRows, accessRows, generatedAt);
    var requestedLevel = level?.Trim().ToLowerInvariant();
    var requestedType = type?.Trim().ToLowerInvariant();
    var filtered = allItems
        .Where(item => string.IsNullOrWhiteSpace(requestedType) || item.Type == requestedType)
        .Where(item => string.IsNullOrWhiteSpace(requestedLevel) || item.Level == requestedLevel)
        .ToArray();
    var items = filtered.Skip((currentPage - 1) * size).Take(size).ToArray();
    return Results.Ok(new AppListEnvelope<AppPendingDto>(
        items, new(currentPage, size, items.Length, currentPage * size < filtered.Length, generatedAt, effectiveConnectionRef,
            pending.Warning is null ? null : [pending.Warning], filtered.Length)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/dashboard", async (
    string? connectionRef,
    string? courseId,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    var generatedAt = DateTimeOffset.UtcNow;
    MoodleConnector.Domain.Registry.ConnectionInfo? resolved;
    try
    {
        resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    }
    catch (MoodleApiException exception) when (exception.ErrorCode == "moodle_connection_not_found")
    {
        return Results.Ok(new AppEnvelope<AppDashboardDto>(
            AppDashboardContractMapper.Empty(null, ["Nenhuma conexÃ£o Moodle foi configurada para esta conta."]),
            new(generatedAt, null)));
    }
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var effectiveConnectionRef = connectionRef ?? resolved.Alias;
    var userId = identity.Id.ToString();

    // Bounded dashboard rule: without an explicit course, only read the course list.
    // Pending/risk indicators require a course scope and are intentionally not fanned out.
    if (string.IsNullOrWhiteSpace(courseId))
    {
        var courses = await mediator.Send(new ListMyCoursesQuery(userId, AppDashboardBudget.MaxCoursesRead, 1), cancellationToken);
        var activeCourses = courses.Items.Count(course => course.Visible != false);
        var warnings = new List<string>();
        if (courses.HasNextPage) warnings.Add("O resumo de cursos estÃ¡ limitado a uma pÃ¡gina para manter o orÃ§amento de leitura.");
        warnings.Add("Selecione um curso para consultar pendÃªncias e indicadores de risco detalhados; nenhuma consulta por aluno foi executada.");
        return Results.Ok(new AppEnvelope<AppDashboardDto>(
            new(new AppDashboardSummaryDto(activeCourses, 0, 0, 0, 0), [], [], [], effectiveConnectionRef, warnings),
            new(generatedAt, effectiveConnectionRef)));
    }

    var course = await mediator.Send(new GetCourseQuery(userId, courseId), cancellationToken);
    if (course is null) return AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.");

    var participants = await mediator.Send(new ListCourseParticipantsQuery(
        userId, courseId, ParticipantStatusFilter.Active, 1, AppDashboardBudget.MaxParticipantsRead, true, false), cancellationToken);
    if (participants is null) return AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.");

    var pending = await mediator.Send(new GetStudentsWithPendingSubmissionsQuery(
        courseId, 0, AppDashboardBudget.MaxParticipantsRead), cancellationToken);
    var pendingRows = pending.Students
        .SelectMany(student => student.PendingAssignments.Select(activity => new AppDashboardPriorityDto(
            $"{effectiveConnectionRef}:{courseId}:{student.StudentId}:{activity.AssignmentId}",
            "Entrega pendente",
            $"{student.FullName} Â· {activity.AssignmentName}",
            activity.IsOverdue ? "risk" : "attention", courseId, student.StudentId)))
        .OrderByDescending(item => item.Level == "risk")
        .ThenBy(item => item.Detail, StringComparer.OrdinalIgnoreCase)
        .Take(AppDashboardBudget.MaxPriorities)
        .ToArray();
    var inactive = participants.Participants.Count(student => student.LastCourseAccessAt is null || student.LastCourseAccessAt < generatedAt.AddDays(-14));
    var dashboardWarnings = new List<string>();
    if (participants.HasMore) dashboardWarnings.Add("O indicador de alunos estÃ¡ limitado ao orÃ§amento de leitura do dashboard.");
    if (pending.Warning is not null) dashboardWarnings.Add(pending.Warning);
    var summary = new AppDashboardSummaryDto(
        course.Visible == false ? 0 : 1,
        pending.Students.Sum(student => student.PendingAssignments.Count),
        0,
        inactive,
        inactive);
    var recent = pendingRows.Take(AppDashboardBudget.MaxActivities)
        .Select(item => new AppDashboardActivityDto(item.Key, item.Title, item.Detail, null, item.CourseId, item.StudentId))
        .ToArray();
    return Results.Ok(new AppEnvelope<AppDashboardDto>(
        new(summary, pendingRows, pendingRows, recent, effectiveConnectionRef, dashboardWarnings),
        new(generatedAt, effectiveConnectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/schools", async (
    string? connectionRef,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMoodleCoursesGateway coursesGateway,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "Conexão Moodle não encontrada.");
    var nodes = await coursesGateway.GetMyCourseHierarchyAsync(identity.Id.ToString(), cancellationToken);
    return Results.Ok(new { data = nodes, meta = new { generatedAt = DateTimeOffset.UtcNow, connectionRef = resolved.Alias } });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/schools/courses", async (
    string categoryPath,
    string? connectionRef,
    int? page,
    int? pageSize,
    HttpContext context,
    ConnectorDbContext dbContext,
    IConnectionRegistry connectionRegistry,
    IMoodleCoursesGateway coursesGateway,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "Conexão Moodle não encontrada.");
    var currentPage = Math.Max(page ?? 1, 1);
    var size = Math.Clamp(pageSize ?? 50, 1, 100);
    var result = await coursesGateway.GetMyCoursesByCategoryAsync(identity.Id.ToString(), categoryPath, size, currentPage, cancellationToken);
    var data = result.Items.Select(course => AppCourseContractMapper.ToDto(course, resolved.Alias)).ToArray();
    return Results.Ok(new AppListEnvelope<AppCourseDto>(data, new(currentPage, size, data.Length, result.HasNextPage, DateTimeOffset.UtcNow, resolved.Alias, null, result.TotalCount)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/courses", async (
    string? connectionRef,
    int? page,
    int? pageSize,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    IConnectionRegistry connectionRegistry,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var currentPage = Math.Max(page ?? 1, 1);
    var size = Math.Clamp(pageSize ?? 20, 1, 100);
    MoodleConnector.Domain.Registry.ConnectionInfo? resolved;
    try
    {
        resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    }
    catch (MoodleApiException exception) when (exception.ErrorCode == "moodle_connection_not_found")
    {
        return Results.Ok(new AppListEnvelope<AppCourseDto>(
            Array.Empty<AppCourseDto>(), new(currentPage, size, 0, false, DateTimeOffset.UtcNow, null,
                ["Nenhuma conexÃ£o Moodle foi configurada para esta conta."])));
    }
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var result = await mediator.Send(new ListMyCoursesQuery(identity.Id.ToString(), size, currentPage), cancellationToken);
    var effectiveConnectionRef = connectionRef ?? resolved.Alias;
    var data = result.Items.Select(course => AppCourseContractMapper.ToDto(course, effectiveConnectionRef)).ToArray();
    return Results.Ok(new AppListEnvelope<AppCourseDto>(data,
        new(currentPage, size, data.Length, result.HasNextPage, DateTimeOffset.UtcNow, effectiveConnectionRef, null, result.TotalCount)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/courses/{connectionRef}/{courseId}", async (
    string connectionRef, string courseId, HttpContext context, ConnectorDbContext dbContext,
    IMediator mediator, IConnectionRegistry connectionRegistry, CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var course = await mediator.Send(new GetCourseQuery(identity.Id.ToString(), courseId), cancellationToken);
    return course is null
        ? AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.")
        : Results.Ok(new AppEnvelope<AppCourseDto>(AppCourseContractMapper.ToDto(course, connectionRef), new(DateTimeOffset.UtcNow, connectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/courses/{connectionRef}/{courseId}/activities", async (
    string connectionRef, string courseId, int? page, int? pageSize, HttpContext context,
    ConnectorDbContext dbContext, IMediator mediator, IConnectionRegistry connectionRegistry,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var resolved = await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken);
    if (resolved is null) return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var result = await mediator.Send(new ListCourseActivitiesQuery(identity.Id.ToString(), courseId, CourseActivityModuleTypes.All, false), cancellationToken);
    if (result is null) return AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.");
    var currentPage = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var data = result.Activities.Skip((currentPage - 1) * size).Take(size)
        .Select(activity => AppCourseContractMapper.ToDto(activity, connectionRef, courseId)).ToArray();
    return Results.Ok(new AppListEnvelope<AppActivityDto>(data,
        new(currentPage, size, data.Length, currentPage * size < result.Total, DateTimeOffset.UtcNow, connectionRef, null, result.Total)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/courses/{connectionRef}/{courseId}/students", async (
    string connectionRef, string courseId, int? page, int? pageSize, HttpContext context,
    ConnectorDbContext dbContext, IMediator mediator, IConnectionRegistry connectionRegistry,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken) is null)
        return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var currentPage = Math.Max(page ?? 1, 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
    var paged = await mediator.Send(new ListCourseParticipantsQuery(identity.Id.ToString(), courseId, ParticipantStatusFilter.Active, currentPage, size, true, true), cancellationToken);
    if (paged is null) return AppErrorResults.NotFound("course_not_found", "Curso nÃ£o encontrado.");
    var data = paged.Participants
        .Select(participant => StudentContractMapper.ToDto(connectionRef, participant,
            new[] { new StudentCourseDto(connectionRef, courseId, courseId, null,
                participant.Suspended == true ? "suspenso" : "ativo", null,
                participant.LastCourseAccessAt, Array.Empty<StudentGradeDto>()) }))
        .ToArray();
    return Results.Ok(new AppListEnvelope<StudentDto>(data,
        new(currentPage, size, data.Length, paged.HasMore,
            DateTimeOffset.UtcNow, connectionRef, null, null)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/courses/{connectionRef}/{courseId}/students/{studentId}", async (
    string connectionRef, string courseId, string studentId, HttpContext context, ConnectorDbContext dbContext,
    IMediator mediator, IConnectionRegistry connectionRegistry, CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (await connectionRegistry.ResolveConnectionAsync(connectionRef, cancellationToken) is null)
        return AppErrorResults.NotFound("connection_not_found", "ConexÃ£o Moodle nÃ£o encontrada.");
    var paged = await mediator.Send(new ListCourseParticipantsQuery(identity.Id.ToString(), courseId, ParticipantStatusFilter.Active, 1, 1000, true, true), cancellationToken);
    var participant = paged?.Participants.FirstOrDefault(p => p.UserId == studentId);
    if (participant is null) return AppErrorResults.NotFound("student_not_found", "Aluno nÃ£o encontrado neste curso.");
    var gradeItems = await mediator.Send(new GetStudentGradeItemsQuery(courseId, studentId), cancellationToken);
    var courseDtos = new[] { new StudentCourseDto(connectionRef, courseId, courseId, null,
        participant.Suspended == true ? "suspenso" : "ativo", null,
        participant.LastCourseAccessAt,
        gradeItems?.Items.Select(StudentContractMapper.ToGradeDto).ToArray() ?? Array.Empty<StudentGradeDto>()) };
    var studentDto = StudentContractMapper.ToDto(connectionRef, participant, courseDtos);
    return Results.Ok(new AppEnvelope<StudentDto>(studentDto, new(DateTimeOffset.UtcNow, connectionRef)));
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/info", (IOptions<MoodleApiOptions> moodleOpts) => Results.Ok(new
{
    ok = true,
    moodleBaseUrlConfigured = !string.IsNullOrWhiteSpace(moodleOpts.Value.BaseUrl)
}));

app.MapGet("/api/reports/student-course", async (
    int reportId,
    int? pageSize,
    HttpContext context,
    IMcpConnectorClientResolver clientResolver,
    IMoodleReportBuilderGateway reportClient,
    CancellationToken cancellationToken) =>
{
    var credential = ReportApiCredentialParser.Parse(context.Request);
    if (credential.ApiKey is null)
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Moodle Connector Reports\", charset=\"UTF-8\"";
        return Results.Json(new
        {
            error = credential.Error,
            message = credential.Error == "invalid_basic_credentials"
                ? "Use o usuario excel-report e a API key do conector como senha."
                : "Informe autenticacao Basica, X-Mcp-Api-Key ou api_key."
        }, statusCode: 401);
    }

    var connectorClient = await clientResolver.ResolveByApiKeyAsync(credential.ApiKey, cancellationToken);
    if (connectorClient is null)
    {
        context.Response.Headers.WWWAuthenticate = "Basic realm=\"Moodle Connector Reports\", charset=\"UTF-8\"";
        return Results.Json(new { error = "invalid_api_key", message = "API key invalida ou inativa." }, statusCode: 401);
    }

    var identity = new ClaimsIdentity("ReportApiKey");
    identity.AddClaim(new Claim("connector_client_id", connectorClient.ClientId));
    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, connectorClient.ClientId));
    context.User = new ClaimsPrincipal(identity);

    try
    {
        var report = await reportClient.DownloadAsync(reportId, pageSize ?? 5000, null, cancellationToken);
        return Results.Ok(new
        {
            atualizadoEm = report.UpdatedAt,
            total = report.Rows.Count,
            dados = report.Rows
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { error = "moodle_report_error", message = ex.Message }, statusCode: 502);
    }
    catch (HttpRequestException ex)
    {
        return Results.Json(new { error = "moodle_unavailable", message = ex.Message }, statusCode: 502);
    }
    catch (JsonException ex)
    {
        return Results.Json(new { error = "invalid_moodle_json", message = "O Moodle devolveu um JSON invalido.", detail = ex.Message }, statusCode: 502);
    }
}).RequireRateLimiting(AdminApiRateLimitPolicy);

app.MapPost("/api/account/register", async (
    RegisterAccountInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IAccountService accountService,
    IPlatformPermissionService platformPermissionService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) ||
        string.IsNullOrWhiteSpace(input.Email) ||
        string.IsNullOrWhiteSpace(input.Password))
        return Results.BadRequest(new { ok = false, error = "Preencha todos os campos obrigatÃ³rios." });

    if (input.Password.Length < 12)
        return Results.BadRequest(new { ok = false, error = "A senha deve ter pelo menos 12 caracteres." });

    try
    {
        var account = await accountService.RegisterAsync(
            new RegisterAccountRequest(input.Name, input.Email, input.Password),
            cancellationToken);
        await SignInAppAccountAsync(context, dbContext, platformPermissionService, account.Id, account.Name, account.Email, cancellationToken);

        return Results.Ok(new
        {
            ok = true,
            redirectUrl = "/?step=moodle"
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { ok = false, error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/account/login", async (
    LoginInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IAccountService accountService,
    IPlatformPermissionService platformPermissionService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(input.Email) ||
        string.IsNullOrWhiteSpace(input.Password))
    {
        return Results.BadRequest(new { ok = false, error = "Preencha e-mail e senha." });
    }

    var account = await accountService.ValidateLoginAsync(
        new LoginAccountRequest(input.Email, input.Password),
        cancellationToken);

    if (account is null)
    {
        return Results.Json(
            new { ok = false, error = "E-mail ou senha invÃ¡lidos." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    await SignInAppAccountAsync(context, dbContext, platformPermissionService, account.Id, account.Name, account.Email, cancellationToken);
    return Results.Ok(new
    {
        ok = true,
        account.Id,
        account.Name,
        account.Email,
        account.HasMoodleConnected
    });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/account/me", async (
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    context.Response.Headers.CacheControl = "no-store";

    var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
    if (profile is null) return Results.NotFound();

    return Results.Ok(new
    {
        ok = true,
        profile.Id,
        profile.Name,
        profile.Email,
        profile.HasMoodleConnected,
        profile.ApiKey,
        hasApiKey = !string.IsNullOrWhiteSpace(profile.ApiKey),
        profile.MoodleConnections
    });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/teams", async (
    HttpContext context,
    ConnectorDbContext dbContext,
    ITeamAccessService teamAccessService,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    var teams = await teamAccessService.GetTeamsAsync(identity.Id, cancellationToken);
    return Results.Ok(new { ok = true, teams });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/teams/{teamId:guid}/invitations", async (
    Guid teamId,
    TeamInvitationInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    ITeamAccessService teamAccessService,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);

    if (string.IsNullOrWhiteSpace(input.Email) || string.IsNullOrWhiteSpace(input.Role))
        return Results.BadRequest(new { ok = false, error = "Informe e-mail e papel do convite." });

    try
    {
        var invitation = await teamAccessService.CreateInvitationAsync(
            new CreateTeamInvitationRequest(
                identity.Id,
                teamId,
                input.Email,
                input.Role,
                input.Scopes ?? [],
                TimeSpan.FromHours(Math.Clamp(input.ExpiresInHours ?? 72, 1, 24 * 30))),
            cancellationToken);
        return Results.Ok(new { ok = true, invitation });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
    catch (InvalidOperationException)
    {
        return Results.Forbid();
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/team-invitations/accept", async (
    TeamInvitationAcceptInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    ITeamAccessService teamAccessService,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(input.Token))
        return Results.BadRequest(new { ok = false, error = "Informe o token do convite." });

    try
    {
        var team = await teamAccessService.AcceptInvitationAsync(identity.Id, identity.Email, input.Token, cancellationToken);
        return Results.Ok(new { ok = true, team });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/permission-groups", async (
    HttpContext context,
    ConnectorDbContext dbContext,
    IPlatformPermissionService permissionService,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    var groups = await permissionService.GetGroupsAsync(identity.Id, cancellationToken);
    return Results.Ok(new { ok = true, groups });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/permission-catalog", async (HttpContext context, ConnectorDbContext dbContext, CancellationToken cancellationToken) =>
{
    if (await ResolveAppIdentityAsync(context, dbContext, cancellationToken) is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        ok = true,
        permissions = PlatformPermissionCatalog.All
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToArray()
    });
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/permission-groups", async (
    CreatePermissionGroupInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IPlatformPermissionService permissionService,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    try
    {
        var group = await permissionService.CreateGroupAsync(
            new CreatePermissionGroupRequest(identity.Id, input.Name, input.Description ?? string.Empty, input.Permissions ?? []), cancellationToken);
        return Results.Ok(new { ok = true, group });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { ok = false, error = ex.Message }); }
    catch (InvalidOperationException) { return Results.Forbid(); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/permission-groups/{groupId:guid}/members", async (
    Guid groupId,
    PermissionGroupMemberInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IPlatformPermissionService permissionService,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    try
    {
        await permissionService.AddMemberAsync(new AddPermissionGroupMemberRequest(identity.Id, groupId, input.UserId), cancellationToken);
        return Results.Ok(new { ok = true });
    }
    catch (InvalidOperationException) { return Results.Forbid(); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPut("/api/users/{userId:guid}/platform-permissions", async (
    Guid userId,
    SetUserPermissionInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IPlatformPermissionService permissionService,
    IAntiforgery antiforgery,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();
    await antiforgery.ValidateRequestAsync(context);
    try
    {
        await permissionService.SetUserPermissionAsync(new SetUserPermissionRequest(identity.Id, userId, input.Permission, input.IsAllowed), cancellationToken);
        return Results.Ok(new { ok = true });
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { ok = false, error = ex.Message }); }
    catch (InvalidOperationException) { return Results.Forbid(); }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/account/api-key/rotate", async (
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    context.Response.Headers.CacheControl = "no-store";
    try
    {
        var apiKey = await accountService.RotateApiKeyAsync(identity.Id, cancellationToken);
        return Results.Ok(new
        {
            ok = true,
            apiKey,
            message = "Nova API key gerada. A chave anterior foi invalidada."
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/account/connect-moodle", async (
    ConnectMoodleInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(input.MoodleAlias) ||
        string.IsNullOrWhiteSpace(input.MoodleBaseUrl) ||
        string.IsNullOrWhiteSpace(input.MoodleUsername) ||
        string.IsNullOrWhiteSpace(input.MoodlePassword))
        return Results.BadRequest(new { ok = false, error = "Preencha alias, URL, usuario e senha do Moodle." });

    try
    {
        var apiKey = await accountService.ConnectMoodleAsync(
            new ConnectMoodleAccountRequest(
                identity.Id,
                input.MoodleAlias,
                input.MoodleBaseUrl,
                input.MoodleUsername,
                input.MoodlePassword,
                input.IsDefault,
                input.CanWrite),
            cancellationToken);

        return Results.Ok(new { ok = true, apiKey, input.MoodleAlias, input.MoodleBaseUrl, input.IsDefault });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPut("/api/account/moodle/{id}", async (
    string id,
    UpdateMoodleInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    if (string.IsNullOrWhiteSpace(input.MoodleAlias) || string.IsNullOrWhiteSpace(input.MoodleBaseUrl))
        return Results.BadRequest(new { ok = false, error = "Preencha alias e URL do Moodle." });

    try
    {
        await accountService.UpdateMoodleAsync(
            new UpdateMoodleAccountRequest(
                identity.Id,
                id,
                input.MoodleAlias,
                input.MoodleBaseUrl,
                input.MoodleUsername,
                input.MoodlePassword,
                input.IsDefault,
                input.CanWrite),
            cancellationToken);

        return Results.Ok(new { ok = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapDelete("/api/account/moodle/{id}", async (
    string id,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        await accountService.DeleteMoodleAsync(identity.Id, id, cancellationToken);
        return Results.Ok(new { ok = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapDelete("/api/account", async (
    [FromBody] DeleteAccountInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        await accountService.DeleteAccountAsync(
            new DeleteAccountRequest(identity.Id, input.Password, input.ConfirmationText),
            cancellationToken);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { ok = true, message = "Conta excluÃ­da definitivamente." });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/auth/login", (string? email, string? returnUrl) =>
{
    return Results.Redirect("/");
});

app.MapPost("/auth/login", async (
    HttpContext context,
    ConnectorDbContext dbContext,
    IAccountService accountService,
    IPlatformPermissionService platformPermissionService,
    CancellationToken cancellationToken) =>
{
    var form = await context.Request.ReadFormAsync(cancellationToken);
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var account = await accountService.ValidateLoginAsync(
        new LoginAccountRequest(email, password),
        cancellationToken);

    if (account is null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrEmpty(email)) qs.Add($"email={Uri.EscapeDataString(email)}");
        if (!string.IsNullOrEmpty(returnUrl)) qs.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        qs.Add("error=" + Uri.EscapeDataString("E-mail ou senha invalidos."));
        return Results.Redirect($"/?{string.Join("&", qs)}");
    }

    await SignInAppAccountAsync(context, dbContext, platformPermissionService, account.Id, account.Name, account.Email, cancellationToken);
    return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl : "/");
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/auth/logout", () =>
{
    return Results.SignOut(authenticationSchemes: new[] { CookieAuthenticationDefaults.AuthenticationScheme });
});

app.MapGet("/api/grading/batches", async (
    HttpContext context,
    ConnectorDbContext dbContext,
    IGradingReviewRepository gradingRepository,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    var batches = await gradingRepository.ListBatchesByCreatorAsync(
        identity.Id.ToString(), cancellationToken);

    return Results.Ok(batches.Select(b => new
    {
        batchJobId = b.Id,
        status = b.Status.ToString(),
        courseId = b.CourseId,
        totalItems = b.TotalItems,
        processedItems = b.ProcessedItems,
        readyItems = b.ReadyItems,
        blockedItems = b.BlockedItems,
        failedItems = b.FailedItems,
        createdAt = b.CreatedAt
    }).ToArray());
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/grading/batches/{id:guid}", async (
    Guid id,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        var result = await mediator.Send(
            new GetAssistedGradingBatchStatusQuery(id, 1, 100), cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapGet("/api/grading/items/{id:guid}", async (
    Guid id,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        var result = await mediator.Send(
            new GetAssistedGradingItemQuery(id), cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPut("/api/grading/items/{id:guid}/review", async (
    Guid id,
    ReviewGradingItemInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        var result = await mediator.Send(
            new UpdateAssistedGradingDraftCommand(
                id,
                input.FinalGrade,
                input.FinalFeedback ?? "",
                input.TeacherDecision ?? "approved",
                input.ReviewNotes,
                input.ExpectedReviewStatus ?? "NotReviewed"),
            cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/grading/batches/{id:guid}/preview", async (
    Guid id,
    PreviewGradingBatchInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    try
    {
        var result = await mediator.Send(
            new CreateGradingLaunchPreviewCommand(
                id,
                input.GradingItemIds ?? [],
                input.OnlyReviewed,
                input.AllowOverwriteExisting),
            cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/api/grading/batches/{id:guid}/confirm", async (
    Guid id,
    ConfirmGradingBatchInput input,
    HttpContext context,
    ConnectorDbContext dbContext,
    IMediator mediator,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolveAppIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    if (input.PendingActionId == Guid.Empty || string.IsNullOrWhiteSpace(input.ConfirmationText))
    {
        return Results.BadRequest(new { ok = false, error = "Informe pendingActionId e confirmationText." });
    }

    try
    {
        var result = await mediator.Send(
            new ConfirmMoodleBatchLaunchCommand(
                input.PendingActionId,
                input.ConfirmationText),
            cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
}).RequireRateLimiting(AppAuthRateLimitPolicy);

app.MapPost("/admin/connector-clients/register", async (
    RegisterConnectorClientInput input,
    HttpContext context,
    IOptions<AdminApiOptions> adminOptions,
    IConnectorClientRegistrationService registrationService,
    CancellationToken cancellationToken) =>
{
    var options = adminOptions.Value;
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        return Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "admin_api_key_not_configured",
            detail: "Configure AdminApi:ApiKey antes de usar o endpoint administrativo.");
    }

    var providedAdminKey = context.Request.Headers[options.HeaderName].ToString();
    if (string.IsNullOrWhiteSpace(providedAdminKey) || !ConstantTimeEquals(providedAdminKey, options.ApiKey))
    {
        return Results.Unauthorized();
    }

    var request = new RegisterConnectorClientRequest(
        input.ClientId,
        input.MoodleAlias,
        input.MoodleBaseUrl,
        input.MoodleUsername,
        input.MoodlePassword,
        input.MoodleTarget,
        input.IsDefault,
        input.CanWrite);

    var result = await registrationService.RegisterOrRotateAsync(request, cancellationToken);

    return Results.Ok(new
    {
        ok = true,
        result.ClientId,
        result.ConnectionId,
        result.MoodleAlias,
        result.ApiKey,
        result.ReplacedExistingClient,
        message = "Credenciais Moodle persistidas e API key emitida/rotacionada para o cliente."
    });
}).RequireRateLimiting(AdminApiRateLimitPolicy);

app.MapMcp(mcpPath);

app.Use(async (context, next) =>
{
    if (context.Request.Path.Value?.Equals("", StringComparison.OrdinalIgnoreCase) == true)
    {
        if (!appV2Enabled)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.Redirect("/");
        return;
    }

    await next();
});

app.MapGet("/app.html", () => appV2Enabled ? Results.Redirect("/") : Results.NotFound());
app.MapGet("/auth.html", (string? tab, string? error) =>
{
    if (!appV2Enabled) return Results.NotFound();
    var query = new List<string>();
    if (string.Equals(tab, "register", StringComparison.OrdinalIgnoreCase)) query.Add("tab=register");
    if (!string.IsNullOrWhiteSpace(error)) query.Add($"error={Uri.EscapeDataString(error)}");
    return Results.Redirect($"/{(query.Count > 0 ? $"?{string.Join("&", query)}" : string.Empty)}");
});
if (appV2Enabled)
{
    app.MapFallbackToFile("/{*path:nonfile}", "index.html");
}

app.Run();

static bool ConstantTimeEquals(string provided, string expected)
{
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

static string NormalizeTaskStatus(string? value) => value switch
{
    "in_progress" => "in_progress",
    "done" => "done",
    _ => "todo"
};

static string NormalizeTaskPriority(string? value) => value switch
{
    "low" => "low",
    "high" => "high",
    "urgent" => "urgent",
    _ => "medium"
};

static string NormalizeCalendarEventType(string? value) => value switch
{
    "meeting" or "alignment" or "delivery" or "training" or "webclass" => value,
    _ => "other"
};

static string NormalizeFollowupKind(string? value) => value switch
{
    "contato" or "orientacao" or "pendencia_conferida" or "ligacao" or "resposta_aluno" or "acompanhamento" => value,
    _ => "acompanhamento"
};

static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

static string GetRateLimitPartitionKey(HttpContext context)
{
    var remoteAddress = GetClientAddress(context);

    return $"{remoteAddress ?? "unknown"}:{context.Request.Path.Value?.ToLowerInvariant()}";
}

static string GetMcpRateLimitPartitionKey(HttpContext context, string apiKeyHeader)
{
    var connectorClientId = context.User.FindFirst("connector_client_id")?.Value;
    if (!string.IsNullOrWhiteSpace(connectorClientId))
    {
        return $"connector:{connectorClientId}";
    }

    var subject = context.User.FindFirst("sub")?.Value ??
                  context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (!string.IsNullOrWhiteSpace(subject))
    {
        return $"subject:{subject}";
    }

    var apiKey = context.Request.Headers[apiKeyHeader].ToString();
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        return $"api-key:{HashRateLimitPartitionValue(apiKey)}";
    }

    return $"ip:{GetClientAddress(context) ?? "unknown"}";
}

static string? GetClientAddress(HttpContext context)
{
    var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
    return string.IsNullOrWhiteSpace(forwardedFor)
        ? context.Connection.RemoteIpAddress?.ToString()
        : forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}

static string HashRateLimitPartitionValue(string value)
{
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(hash);
}

static async Task<bool> IsMcpDiscoveryRequestAsync(HttpContext context)
{
    if (!HttpMethods.IsPost(context.Request.Method) ||
        context.Request.ContentLength is 0)
    {
        return false;
    }

    context.Request.EnableBuffering();
    try
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (!document.RootElement.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var method = methodElement.GetString();
        return string.Equals(method, "initialize", StringComparison.Ordinal) ||
               string.Equals(method, "notifications/initialized", StringComparison.Ordinal) ||
               string.Equals(method, "tools/list", StringComparison.Ordinal);
    }
    catch (JsonException)
    {
        return false;
    }
    finally
    {
        context.Request.Body.Position = 0;
    }
}

static bool HasMcpCredentials(HttpContext context, McpServerSecurityOptions securityOptions)
{
    var authHeader = context.Request.Headers.Authorization.ToString();
    if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return !string.IsNullOrWhiteSpace(context.Request.Headers[securityOptions.ApiKeyHeader].ToString());
}

static async Task<bool> TryWriteMcpOauthToolChallengeAsync(
    HttpContext context,
    string error,
    string errorDescription)
{
    var response = await BuildMcpOauthToolChallengeResponseAsync(context, error, errorDescription);
    if (response is null)
    {
        return false;
    }

    context.Response.StatusCode = StatusCodes.Status200OK;
    await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    await AuditMcpAuthorizationFailureAsync(context, error, errorDescription);
    return true;
}

static async Task<JsonObject?> BuildMcpOauthToolChallengeResponseAsync(
    HttpContext context,
    string error,
    string errorDescription)
{
    if (!HttpMethods.IsPost(context.Request.Method) ||
        context.Request.ContentLength is 0)
    {
        return null;
    }

    context.Request.EnableBuffering();
    try
    {
        using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (!document.RootElement.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String ||
            !string.Equals(methodElement.GetString(), "tools/call", StringComparison.Ordinal))
        {
            return null;
        }

        JsonNode? id = null;
        if (document.RootElement.TryGetProperty("id", out var idElement))
        {
            id = JsonNode.Parse(idElement.GetRawText());
        }

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JsonObject
            {
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = errorDescription
                    }
                },
                ["_meta"] = new JsonObject
                {
                    ["mcp/www_authenticate"] = new JsonArray
                    {
                        BuildMcpOauthAuthenticateChallenge(context, error, errorDescription)
                    }
                },
                ["isError"] = true
            }
        };
    }
    catch (JsonException)
    {
        return null;
    }
    finally
    {
        context.Request.Body.Position = 0;
    }
}

static Task AuditMcpAuthorizationFailureAsync(HttpContext context, string reason, string message)
{
    var principal = context.User;
    var actorSubject = principal.FindFirstValue("sub")
        ?? principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("connector_client_id");
    var actorEmail = principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("email")
        ?? principal.FindFirstValue("preferred_username");
    var authenticationType = principal.Identity?.AuthenticationType;

    var audit = context.RequestServices.GetRequiredService<IAuthorizationAuditService>();
    return audit.RecordFailureAsync(new AuthorizationFailureAuditRequest(
        "mcp_auth",
        reason,
        message,
        actorSubject,
        actorEmail,
        context.Request.Path.Value,
        authenticationType), context.RequestAborted);
}

static async Task WriteMcpRequestParseErrorAsync(HttpContext context, ILogger logger, Exception exception, string errorCode, string message)
{
    logger.LogWarning(exception, "MCP request parsing failed for path {Path} with error code {ErrorCode}.", context.Request.Path, errorCode);

    await context.Response.WriteAsJsonAsync(new
    {
        ok = false,
        error = errorCode,
        message
    }, context.RequestAborted);
}

static IResult BuildOAuthProtectedResourceMetadata(
    HttpContext context,
    IOptions<OAuthBrokerOptions> oauth,
    IOptions<McpServerSecurityOptions> security)
{
    if (!security.Value.RequireJwt)
    {
        return Results.NotFound(new
        {
            error = "oauth_not_configured",
            message = "OAuth/JWT nao esta configurado para o endpoint MCP."
        });
    }

    var publicBaseUrl = GetPublicBaseUrl(context);
    var authority = ResolveOAuthIssuer(oauth.Value, publicBaseUrl);
    var scopes = GetMcpOauthScopes(oauth.Value);
    return Results.Json(new
    {
        resource = $"{publicBaseUrl}{mcpPath}",
        resource_name = "MoodleConnector",
        authorization_servers = new[] { authority },
        scopes_supported = scopes,
        bearer_methods_supported = new[] { "header" },
        resource_documentation = $"{publicBaseUrl}/"
    });
}

static IResult BuildOAuthAuthorizationServerMetadata(HttpContext context, IOptions<OAuthBrokerOptions> oauth)
{
    var publicBaseUrl = GetPublicBaseUrl(context);
    var issuer = ResolveOAuthIssuer(oauth.Value, publicBaseUrl);
    return Results.Json(new
    {
        issuer,
        authorization_endpoint = $"{publicBaseUrl}/authorize",
        token_endpoint = $"{publicBaseUrl}/token",
        jwks_uri = $"{publicBaseUrl}/.well-known/jwks",
        response_types_supported = new[] { "code" },
        grant_types_supported = new[] { "authorization_code", "refresh_token" },
        code_challenge_methods_supported = new[] { "S256" },
        token_endpoint_auth_methods_supported = new[] { "none" },
        scopes_supported = GetMcpOauthScopes(oauth.Value),
        client_id_metadata_document_supported = false
    });
}

static string[] GetMcpOauthScopes(OAuthBrokerOptions? options = null)
{
    var audienceScope = options?.ScopeName;
    if (string.IsNullOrWhiteSpace(audienceScope))
    {
        audienceScope = "moodle-mcp-audience";
    }

    return new[]
        {
            "openid", "profile", "email", "offline_access", audienceScope.Trim(),
            // Moodle granular scopes (from MoodleScopePolicies)
            MoodleScopePolicies.ReadCourses,
            MoodleScopePolicies.ReadStudents,
            MoodleScopePolicies.ReadGroups,
            MoodleScopePolicies.ReadAccess,
            MoodleScopePolicies.ReadContents,
            MoodleScopePolicies.ReadResources,
            MoodleScopePolicies.ReadActivities,
            MoodleScopePolicies.ReadAssignments,
            MoodleScopePolicies.ReadSubmissions,
            MoodleScopePolicies.ReadQuizzes,
            MoodleScopePolicies.ReadScorms,
            MoodleScopePolicies.WriteMessages,
            MoodleScopePolicies.WriteAssignmentsFeedback,
            MoodleScopePolicies.WriteAssignmentsGrade,
            MoodleScopePolicies.WriteCourseContent,
            MoodleScopePolicies.Admin
        }
        .Where(scope => !string.IsNullOrWhiteSpace(scope))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void AddOAuthSecuritySchemes(ModelContextProtocol.Protocol.Tool tool)
{
    tool.Meta ??= new JsonObject();
    if (tool.Meta.ContainsKey("securitySchemes"))
    {
        return;
    }

    tool.Meta["securitySchemes"] = CreateOAuthSecuritySchemesNode();
}

static JsonArray CreateOAuthSecuritySchemesNode()
{
    var scopes = new JsonArray();
    foreach (var scope in GetMcpOauthScopes())
    {
        scopes.Add(scope);
    }

    return new JsonArray
    {
        new JsonObject
        {
            ["type"] = "oauth2",
            ["scopes"] = scopes
        }
    };
}

static void AddGradingReviewToolMetadata(ModelContextProtocol.Protocol.Tool tool)
{
    if (!string.Equals(tool.Name, MoodleGradingReviewAppMetadata.ToolName, StringComparison.Ordinal))
    {
        return;
    }

    tool.Meta ??= new JsonObject();
    var toolMeta = MoodleGradingReviewAppMetadata.CreateToolMeta();
    tool.Meta["ui"] = toolMeta["ui"]?.DeepClone();
    tool.Meta["openai/outputTemplate"] = MoodleGradingReviewAppMetadata.ResourceUri;
}

static string GetPublicBaseUrl(HttpContext context)
{
    var configuredBaseUrl = BuildPublicBaseUrlFromAppDomain(
        Environment.GetEnvironmentVariable("APP_DOMAIN") ??
        context.RequestServices.GetRequiredService<IConfiguration>()["APP_DOMAIN"]);
    if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
    {
        return configuredBaseUrl;
    }

    return $"{context.Request.Scheme}://{context.Request.Host}";
}

static void SetMcpOauthAuthenticateHeader(HttpContext context)
{
    context.Response.Headers.WWWAuthenticate =
        BuildMcpOauthAuthenticateChallenge(
            context,
            "invalid_token",
            "Token ausente, expirado ou invalido para o Moodle Connector.");
}

static string BuildMcpOauthAuthenticateChallenge(
    HttpContext context,
    string error,
    string errorDescription)
{
    return string.Join(", ", new[]
    {
        $"Bearer resource_metadata=\"{GetPublicBaseUrl(context)}/.well-known/oauth-protected-resource/mcp\"",
        $"scope=\"{EscapeWwwAuthenticateValue(string.Join(' ', GetMcpOauthScopes()))}\"",
        $"error=\"{EscapeWwwAuthenticateValue(error)}\"",
        $"error_description=\"{EscapeWwwAuthenticateValue(errorDescription)}\""
    });
}

static string EscapeWwwAuthenticateValue(string value)
{
    return value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);
}

static string? BuildPublicBaseUrlFromAppDomain(string? appDomain)
{
    if (string.IsNullOrWhiteSpace(appDomain))
    {
        return null;
    }

    var normalized = appDomain.Trim();
    if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        return normalized.TrimEnd('/');
    }

    return $"https://{normalized.TrimEnd('/')}";
}

static string ResolveOAuthIssuer(OAuthBrokerOptions options, string publicBaseUrl)
{
    return string.IsNullOrWhiteSpace(options.Issuer)
        ? publicBaseUrl.TrimEnd('/')
        : options.Issuer.TrimEnd('/');
}

static string ResolveOAuthAudience(OAuthBrokerOptions options, string publicBaseUrl, string mcpPath)
{
    return string.IsNullOrWhiteSpace(options.Audience)
        ? $"{publicBaseUrl.TrimEnd('/')}{mcpPath}"
        : options.Audience.Trim();
}

static X509Certificate2 LoadOrCreateOAuthCertificate(
    OAuthBrokerOptions options,
    string name,
    string subject,
    X509KeyUsageFlags keyUsage)
{
    var storagePath = string.IsNullOrWhiteSpace(options.KeyStoragePath)
        ? Path.Combine(AppContext.BaseDirectory, "App_Data", "oauth")
        : options.KeyStoragePath.Trim();

    if (!Path.IsPathRooted(storagePath))
    {
        storagePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, storagePath));
    }

    Directory.CreateDirectory(storagePath);

    var certificatePath = Path.Combine(storagePath, $"{name}.pfx");
    const X509KeyStorageFlags storageFlags = X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable;
    if (File.Exists(certificatePath))
    {
        return X509CertificateLoader.LoadPkcs12FromFile(certificatePath, string.Empty, storageFlags, Pkcs12LoaderLimits.Defaults);
    }

    using var rsa = RSA.Create(2048);
    var request = new CertificateRequest(
        $"CN={subject}",
        rsa,
        HashAlgorithmName.SHA256,
        RSASignaturePadding.Pkcs1);

    request.CertificateExtensions.Add(new X509KeyUsageExtension(keyUsage, critical: true));
    request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

    var years = Math.Clamp(options.CertificateYears, 1, 20);
    using var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddMinutes(-5),
        DateTimeOffset.UtcNow.AddYears(years));
    var pfx = certificate.Export(X509ContentType.Pfx, string.Empty);
    File.WriteAllBytes(certificatePath, pfx);

    return X509CertificateLoader.LoadPkcs12(pfx, string.Empty, storageFlags, Pkcs12LoaderLimits.Defaults);
}

static void ValidateMcpAuthConfiguration(
    IWebHostEnvironment environment,
    McpServerSecurityOptions security,
    string oauthIssuer,
    string oauthAudience,
    OAuthBrokerOptions oauth)
{
    if (!security.RequireApiKey && !security.RequireJwt)
    {
        throw new InvalidOperationException("Ative pelo menos um metodo de autenticacao MCP (JWT ou API key).");
    }

    if (!security.RequireJwt)
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(oauthIssuer))
    {
        throw new InvalidOperationException("OAuth:Issuer ou APP_DOMAIN e obrigatorio quando McpServerSecurity:RequireJwt=true.");
    }

    if (string.IsNullOrWhiteSpace(oauthAudience))
    {
        throw new InvalidOperationException("OAuth:Audience ou APP_DOMAIN e obrigatorio quando McpServerSecurity:RequireJwt=true.");
    }

    var isDevLike = environment.IsDevelopment() || environment.IsEnvironment("Testing");
    if (!oauth.RequireHttpsMetadata && !isDevLike)
    {
        throw new InvalidOperationException("OAuth:RequireHttpsMetadata=false so e permitido em Development/Testing.");
    }

    if (!Uri.TryCreate(oauthIssuer, UriKind.Absolute, out var issuerUri))
    {
        throw new InvalidOperationException("OAuth:Issuer deve ser uma URL absoluta quando McpServerSecurity:RequireJwt=true.");
    }

    if (!Uri.TryCreate(oauthAudience, UriKind.Absolute, out var audienceUri))
    {
        throw new InvalidOperationException("OAuth:Audience deve ser uma URL absoluta quando McpServerSecurity:RequireJwt=true.");
    }

    if (!string.IsNullOrWhiteSpace(oauth.ChatGptRedirectUri) &&
        !Uri.TryCreate(oauth.ChatGptRedirectUri, UriKind.Absolute, out _))
    {
        throw new InvalidOperationException("OAuth:ChatGptRedirectUri deve ser uma URL absoluta.");
    }

    if (!isDevLike)
    {
        if (issuerUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("OAuth:Issuer deve usar HTTPS em producao.");
        }

        if (audienceUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("OAuth:Audience deve usar HTTPS em producao.");
        }

        if (string.IsNullOrWhiteSpace(oauth.ChatGptRedirectUri))
        {
            throw new InvalidOperationException("OAuth:ChatGptRedirectUri e obrigatorio em producao quando JWT/OAuth esta habilitado.");
        }

        var redirectUri = new Uri(oauth.ChatGptRedirectUri);
        if (redirectUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("OAuth:ChatGptRedirectUri deve usar HTTPS em producao.");
        }
    }
}

static void ValidateProductionSecuritySettings(
    IWebHostEnvironment environment,
    PostgresOptions? postgres,
    ConnectorSecretsOptions? secrets,
    AdminApiOptions? adminApi)
{
    var isDevLike = environment.IsDevelopment() || environment.IsEnvironment("Testing");
    if (isDevLike)
    {
        return;
    }

    if (postgres is not null && !string.IsNullOrWhiteSpace(postgres.ConnectionString))
    {
        var connStrLower = postgres.ConnectionString.ToLowerInvariant();
        if (connStrLower.Contains("password=postgres") || connStrLower.Contains("username=postgres"))
        {
            throw new InvalidOperationException("SeguranÃ§a de ProduÃ§Ã£o: Postgres ConnectionString nÃ£o pode utilizar o usuÃ¡rio ou senha padrÃ£o 'postgres' em ambiente de produÃ§Ã£o.");
        }
    }

    if (secrets is not null && !string.IsNullOrWhiteSpace(secrets.EncryptionKeyBase64))
    {
        const string defaultKey = "MDEyMzQ1Njc4OUFCQ0RFRjAxMjM0NTY3ODlBQkNERUY=";
        if (secrets.EncryptionKeyBase64 == defaultKey)
        {
            throw new InvalidOperationException("SeguranÃ§a de ProduÃ§Ã£o: ConnectorSecrets:EncryptionKeyBase64 nÃ£o pode utilizar a chave AES de exemplo em ambiente de produÃ§Ã£o.");
        }
    }

    if (adminApi is not null && !string.IsNullOrWhiteSpace(adminApi.ApiKey))
    {
        var apiKeyLower = adminApi.ApiKey.ToLowerInvariant();
        if (apiKeyLower == "troque-este-valor-em-producao" ||
            apiKeyLower.Contains("change-me") ||
            apiKeyLower.Contains("troque-este-valor"))
        {
            throw new InvalidOperationException("SeguranÃ§a de ProduÃ§Ã£o: AdminApi:ApiKey nÃ£o pode utilizar o valor padrÃ£o ou conter expressÃµes como 'change-me' ou 'troque-este-valor' em ambiente de produÃ§Ã£o.");
        }
    }
}

static async Task EnrichMcpPrincipalFromLocalAccountAsync(HttpContext context, CancellationToken cancellationToken)
{
    var principal = context.User;
    if (principal.Identity?.IsAuthenticated != true)
    {
        return;
    }

    var email = principal.FindFirstValue(ClaimTypes.Email)
        ?? principal.FindFirstValue("email")
        ?? principal.FindFirstValue("preferred_username");
    if (string.IsNullOrWhiteSpace(email))
    {
        return;
    }

    var dbContext = context.RequestServices.GetRequiredService<ConnectorDbContext>();
    var normalizedEmail = NormalizeEmail(email);
    var account = await dbContext.UserAccounts
        .AsNoTracking()
        .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
    if (account is null || string.IsNullOrWhiteSpace(account.ConnectorClientId))
    {
        return;
    }

    if (principal.Identity is not ClaimsIdentity identity)
    {
        return;
    }

    foreach (var existingClaim in identity.FindAll("connector_client_id").ToArray())
    {
        identity.RemoveClaim(existingClaim);
    }
    identity.AddClaim(new Claim("connector_client_id", account.ConnectorClientId));

    var memberships = await dbContext.TeamMemberships
        .AsNoTracking()
        .Where(item => item.UserId == account.Id && item.IsActive)
        .ToArrayAsync(cancellationToken);
    foreach (var membership in memberships)
    {
        identity.AddClaim(new Claim("team_id", membership.TeamId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, membership.Role));
        foreach (var scope in JsonSerializer.Deserialize<string[]>(membership.ScopesJson) ?? [])
        {
            if (!principal.FindAll("scope").Any(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains(scope, StringComparer.OrdinalIgnoreCase)))
            {
                identity.AddClaim(new Claim("scope", scope));
            }
        }
    }

    var canWrite = await dbContext.ConnectorClients
        .AsNoTracking()
        .AnyAsync(client =>
            client.ClientId == account.ConnectorClientId &&
            client.IsActive &&
            client.CanWrite,
            cancellationToken);
    if (canWrite && !principal.FindAll("scope").Any(claim =>
            claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(value, "moodle.write", StringComparison.OrdinalIgnoreCase))))
    {
        identity.AddClaim(new Claim("scope", "moodle.write"));
    }
}

static async Task<AppIdentity?> ResolveAppIdentityAsync(
    HttpContext context,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken)
{
    var principal = context.User;
    if (principal?.Identity?.IsAuthenticated == true)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email")
            ?? principal.FindFirstValue("preferred_username");

        var name = principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username")
            ?? email;

        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(OpenIddictConstants.Claims.Subject)
            ?? principal.FindFirstValue("sub");
        if (Guid.TryParse(subject, out var userId))
        {
            var accountById = await dbContext.UserAccounts.FindAsync([userId], cancellationToken);
            if (accountById is not null)
            {
                return new AppIdentity(
                    accountById.Id,
                    accountById.Name,
                    accountById.Email,
                    accountById.ConnectorClientId);
            }
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalizedEmail = NormalizeEmail(email);
            var accountByEmail = await dbContext.UserAccounts
                .SingleOrDefaultAsync(account => account.Email == normalizedEmail, cancellationToken);
            if (accountByEmail is not null)
            {
                if (!string.IsNullOrWhiteSpace(name) &&
                    !string.Equals(accountByEmail.Name, name.Trim(), StringComparison.Ordinal))
                {
                    accountByEmail.Name = name.Trim();
                    accountByEmail.UpdatedAtUtc = DateTimeOffset.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return new AppIdentity(
                    accountByEmail.Id,
                    accountByEmail.Name,
                    accountByEmail.Email,
                    accountByEmail.ConnectorClientId);
            }
        }
    }

    return null;
}

static async Task SignInAppAccountAsync(HttpContext context, ConnectorDbContext dbContext, IPlatformPermissionService platformPermissionService, Guid id, string name, string email, CancellationToken cancellationToken)
{
    // Existing accounts may predate the platform-permission groups. Reconcile the
    // defaults before issuing claims so MCP tools do not receive a stale/empty set.
    await platformPermissionService.EnsureDefaultPermissionsAsync(id, cancellationToken);

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, id.ToString()),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Email, email),
        new(OpenIddictConstants.Claims.Subject, id.ToString()),
        new(OpenIddictConstants.Claims.Name, name),
        new(OpenIddictConstants.Claims.Email, email)
    };

    var memberships = await dbContext.TeamMemberships
        .AsNoTracking()
        .Where(item => item.UserId == id && item.IsActive)
        .ToArrayAsync(cancellationToken);
    foreach (var membership in memberships)
    {
        claims.Add(new Claim("team_id", membership.TeamId.ToString()));
        claims.Add(new Claim(ClaimTypes.Role, membership.Role));
        foreach (var scope in JsonSerializer.Deserialize<string[]>(membership.ScopesJson) ?? [])
            claims.Add(new Claim("scope", scope));
    }

    var groupPermissions = await (from membership in dbContext.PermissionGroupMemberships.AsNoTracking()
                                  join permission in dbContext.PermissionGroupPermissions.AsNoTracking() on membership.GroupId equals permission.GroupId
                                  where membership.UserId == id
                                  select permission.Permission).ToArrayAsync(cancellationToken);
    foreach (var permission in groupPermissions.Distinct(StringComparer.OrdinalIgnoreCase))
        claims.Add(new Claim("platform_permission", permission));
    var userOverrides = await dbContext.UserPermissionOverrides.AsNoTracking().Where(item => item.UserId == id).ToArrayAsync(cancellationToken);
    foreach (var permission in userOverrides)
        claims.Add(new Claim(permission.IsAllowed ? "platform_permission" : "platform_permission_deny", permission.Permission));

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
}



static bool IsLocalReturnUrl(string? returnUrl)
{
    return !string.IsNullOrWhiteSpace(returnUrl) &&
           returnUrl.StartsWith("/", StringComparison.Ordinal) &&
           !returnUrl.StartsWith("//", StringComparison.Ordinal);
}

static IEnumerable<string> GetOAuthClaimDestinations(Claim claim)
{
    switch (claim.Type)
    {
        case OpenIddictConstants.Claims.Subject:
        case OpenIddictConstants.Claims.Name:
        case OpenIddictConstants.Claims.Email:
            return new[] { OpenIddictConstants.Destinations.AccessToken, OpenIddictConstants.Destinations.IdentityToken };
        case "connector_client_id":
            return new[] { OpenIddictConstants.Destinations.AccessToken };
        default:
            return new[] { OpenIddictConstants.Destinations.AccessToken };
    }
}

static async Task SeedChatGptOAuthClientAsync(
    IServiceProvider services,
    ILogger logger,
    string appDomain,
    IWebHostEnvironment environment)
{
    var oauth = services.GetRequiredService<IOptions<OAuthBrokerOptions>>().Value;
    if (string.IsNullOrWhiteSpace(oauth.ChatGptRedirectUri))
    {
        logger.LogWarning("OAuth:ChatGptRedirectUri nao configurado; o client OAuth do ChatGPT nao foi seedado.");
        return;
    }

    var publicBaseUrl = BuildPublicBaseUrlFromAppDomain(appDomain) ??
                        (environment.IsEnvironment("Testing") ? "http://localhost" : string.Empty);
    var oauthAudience = ResolveOAuthAudience(oauth, publicBaseUrl, "/mcp");

    var manager = services.GetRequiredService<IOpenIddictApplicationManager>();
    var descriptor = new OpenIddictApplicationDescriptor
    {
        ClientId = string.IsNullOrWhiteSpace(oauth.ChatGptClientId) ? "chatgpt-mcp" : oauth.ChatGptClientId,
        DisplayName = "ChatGPT Moodle Connector",
        ClientType = OpenIddictConstants.ClientTypes.Public,
        ConsentType = OpenIddictConstants.ConsentTypes.Implicit
    };

    descriptor.RedirectUris.Add(new Uri(oauth.ChatGptRedirectUri));
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Email);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Scopes.Profile);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access");
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + oauth.ScopeName);
    // Moodle granular scopes
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadCourses);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadStudents);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadGroups);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadAccess);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadContents);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadResources);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadActivities);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadAssignments);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadSubmissions);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadQuizzes);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.ReadScorms);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.WriteMessages);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.WriteAssignmentsFeedback);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.WriteAssignmentsGrade);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.WriteCourseContent);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + MoodleScopePolicies.Admin);
    descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Resource + oauthAudience);
    descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

    var existing = await manager.FindByClientIdAsync(descriptor.ClientId);
    if (existing is null)
    {
        await manager.CreateAsync(descriptor);
        return;
    }

    await manager.UpdateAsync(existing, descriptor);
}

static bool HasAppPermission(HttpContext context, string permission)
{
    if (context.User.FindAll("platform_permission_deny").Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase)))
        return false;
    if (context.User.FindAll(ClaimTypes.Role)
        .Concat(context.User.FindAll("role"))
        .Any(x => string.Equals(x.Value, "admin", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(x.Value, "administrator", StringComparison.OrdinalIgnoreCase)))
        return true;
    return context.User.FindAll("platform_permission")
        .Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase));
}

static bool HasPlatformToolPermission(ClaimsPrincipal? principal, string permission)
{
    if (principal is null) return false;
    if (principal.FindAll("platform_permission_deny").Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase)))
        return false;
    return principal.FindAll("platform_permission")
        .Any(x => string.Equals(x.Value, permission, StringComparison.OrdinalIgnoreCase));
}

public sealed record AppIdentity(Guid Id, string Name, string Email, string? ConnectorClientId);
public sealed record AppEnvelope<T>(T Data, AppMeta Meta);
public sealed record AppListEnvelope<T>(IReadOnlyList<T> Data, AppListMeta Meta);
public sealed record AppMeta(DateTimeOffset GeneratedAt, string? ConnectionRef);
public sealed record AppListMeta(
    int Page,
    int PageSize,
    int Returned,
    bool HasMore,
    DateTimeOffset GeneratedAt,
    string? ConnectionRef,
    IReadOnlyList<string>? Warnings = null,
    int? Total = null);
public sealed record AppSessionDto(bool Authenticated, AppUserDto? User);
public sealed record AppUserDto(Guid Id, string Name, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions);

public static class AppPermissionCatalog
{
    public const string DashboardView = "dashboard.view";
    public const string CoursesView = "courses.view";
    public const string StudentsView = "students.view";
    public const string StudentsFollowupWrite = "students.followup.write";
    public const string TasksManage = "tasks.manage";
    public const string AgendaManage = "agenda.manage";
    public const string MessagesPrepare = "messages.prepare";
    public const string ReportsView = "reports.view";
    public const string ConnectionsManage = "connections.manage";
    public const string SettingsView = "settings.view";
    public const string AdminView = "admin.view";

    private static readonly string[] All = [
        DashboardView, CoursesView, StudentsView, StudentsFollowupWrite, TasksManage,
        AgendaManage, MessagesPrepare, ReportsView, ConnectionsManage, SettingsView, AdminView];
}
public sealed record AppConnectionDto(string ConnectionId, string ConnectionRef, string Alias, string Host, string Status, bool IsDefault, IReadOnlyList<string> Capabilities, DateTimeOffset? LastValidatedAt);
public sealed record AppDeleteConnectionInput(bool DeleteLinkedData, string? ConfirmationText);

public partial class Program;

public sealed record RegisterConnectorClientInput(
    string ClientId,
    string MoodleAlias,
    string MoodleBaseUrl,
    string MoodleUsername,
    string MoodlePassword,
    string MoodleTarget,
    bool IsDefault,
    bool CanWrite);

public sealed record RegisterAccountInput(string Name, string Email, string Password);
public sealed record LoginInput(string Email, string Password);
public sealed record DeleteAccountInput(string Password, string ConfirmationText);
public sealed record ConnectMoodleInput(string MoodleAlias, string MoodleBaseUrl, string MoodleUsername, string MoodlePassword, bool IsDefault = false, bool CanWrite = false);
public sealed record UpdateMoodleInput(string MoodleAlias, string MoodleBaseUrl, string? MoodleUsername, string? MoodlePassword, bool IsDefault = false, bool CanWrite = false);
public sealed record TeamInvitationInput(string Email, string Role, string[]? Scopes = null, int? ExpiresInHours = null);
public sealed record TeamInvitationAcceptInput(string Token);
public sealed record CreatePermissionGroupInput(string Name, string? Description, string[]? Permissions = null);
public sealed record PermissionGroupMemberInput(Guid UserId);
public sealed record SetUserPermissionInput(string Permission, bool IsAllowed);

public sealed record ReviewGradingItemInput(decimal? FinalGrade, string? FinalFeedback, string? TeacherDecision, string? ReviewNotes, string? ExpectedReviewStatus);
public sealed record PreviewGradingBatchInput(
    Guid[]? GradingItemIds,
    bool OnlyReviewed = true,
    bool AllowOverwriteExisting = false);
public sealed record ConfirmGradingBatchInput(Guid PendingActionId, string ConfirmationText);


