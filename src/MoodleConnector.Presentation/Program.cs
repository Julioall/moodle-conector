using System.Security.Cryptography;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MoodleConnector.Application;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.PendingActions;
using MoodleConnector.Infrastructure;
using MoodleConnector.Presentation.Configuration;
using MoodleConnector.Presentation.Security;
using MoodleConnector.Presentation.Tools;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
const string PortalAuthRateLimitPolicy = "portal-auth";
const string AdminApiRateLimitPolicy = "admin-api";

builder.Services.AddHttpContextAccessor();

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
    .AddOptions<PendingActionOptions>()
    .Bind(builder.Configuration.GetSection(PendingActionOptions.SectionName));

builder.Services
    .AddOptions<ConnectorRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(ConnectorRateLimitOptions.SectionName));

builder.Services.AddSingleton<McpFixedWindowRateLimiter>();

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
    options.AddPolicy(PortalAuthRateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Clamp(rateLimitOptions.PortalAuthPermitLimit, 1, 1000),
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

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = "moodle-connector-portal";
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
    .AddMcpServer()
    .WithHttpTransport()
    .WithRequestFilters(filters =>
    {
        filters.AddListToolsFilter(next => async (request, cancellationToken) =>
        {
            var result = await next(request, cancellationToken);
            if (request.Services is null)
            {
                return result;
            }

            var security = request.Services.GetRequiredService<IOptions<McpServerSecurityOptions>>().Value;
            if (security.RequireJwt)
            {
                foreach (var tool in result.Tools)
                {
                    AddOAuthSecuritySchemes(tool);
                }
            }

            return result;
        });
    })
    .WithTools<MoodleCoursesTools>()
    .WithTools<MoodleParticipantsTools>()
    .WithTools<MoodleCourseContentsTools>()
    .WithTools<MoodleCourseActivitiesTools>()
    .WithTools<MoodleAssignmentSubmissionsTools>();

var featureOptions = builder.Configuration.GetSection(FeatureOptions.SectionName).Get<FeatureOptions>() ?? new FeatureOptions();
if (featureOptions.DemoToolsEnabled)
{
    mcpServerBuilder.WithTools<DemoPendingActionTools>();
}

var app = builder.Build();

app.UseForwardedHeaders();

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

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "connector-api-key"));
            isAuthenticated = true;
        }
        else if (!securityOptions.RequireJwt || !hasBearerToken)
        {
            if (securityOptions.RequireJwt &&
                await TryWriteMcpOauthToolChallengeAsync(
                    context,
                    "invalid_token",
                    "API key invalida. Faça login via OAuth para continuar."))
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
                    "JWT ausente, expirado ou rejeitado pelo broker OAuth. Faça login novamente."))
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
                    "Autenticação OAuth necessária para usar as tools do Moodle Connector."))
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
                    "Autenticação OAuth necessária para usar as tools do Moodle Connector."))
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
            "Requisição MCP invalida. Envie um payload JSON-RPC 2.0 valido.");
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
            "Requisição MCP invalida. Nao foi possivel ler o payload recebido.");
    }
});

app.UseRateLimiter();

app.MapGet("/api/status", (HttpContext context, IOptions<McpServerSecurityOptions> security, IOptions<OAuthBrokerOptions> oauth) =>
{
    var publicBaseUrl = GetPublicBaseUrl(context);
    return Results.Ok(new
    {
        ok = true,
        service = "moodle-gpt-connector",
        status = "online",
        transport = "mcp-streamable-http",
        endpoint = mcpPath,
        source = "aspnetcore-mcp",
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

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

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

    var identity = await ResolvePortalIdentityAsync(context, dbContext, cancellationToken);
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
    claims.SetClaim("connector_client_id", identity.Id.ToString());

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

// ─── Portal API ────────────────────────────────────────────────────────────────

app.MapGet("/api/info", (IOptions<MoodleApiOptions> moodleOpts) => Results.Ok(new
{
    ok = true,
    moodleBaseUrlConfigured = !string.IsNullOrWhiteSpace(moodleOpts.Value.BaseUrl)
}));

app.MapPost("/api/account/register", async (
    RegisterAccountInput input,
    HttpContext context,
    IAccountService accountService,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(input.Name) ||
        string.IsNullOrWhiteSpace(input.Email) ||
        string.IsNullOrWhiteSpace(input.Password))
        return Results.BadRequest(new { ok = false, error = "Preencha todos os campos obrigatórios." });

    if (input.Password.Length < 12)
        return Results.BadRequest(new { ok = false, error = "A senha deve ter pelo menos 12 caracteres." });

    try
    {
        var account = await accountService.RegisterAsync(
            new RegisterAccountRequest(input.Name, input.Email, input.Password),
            cancellationToken);
        await SignInPortalAccountAsync(context, account.Id, account.Name, account.Email);

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
}).RequireRateLimiting(PortalAuthRateLimitPolicy);

app.MapPost("/api/account/login", async (
    LoginInput input,
    HttpContext context,
    IAccountService accountService,
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
            new { ok = false, error = "E-mail ou senha inválidos." },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    await SignInPortalAccountAsync(context, account.Id, account.Name, account.Email);
    return Results.Ok(new
    {
        ok = true,
        account.Id,
        account.Name,
        account.Email,
        account.HasMoodleConnected
    });
}).RequireRateLimiting(PortalAuthRateLimitPolicy);

app.MapGet("/api/account/me", async (
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolvePortalIdentityAsync(context, dbContext, cancellationToken);
    if (identity is null) return Results.Unauthorized();

    var profile = await accountService.GetProfileAsync(identity.Id, cancellationToken);
    if (profile is null) return Results.NotFound();

    return Results.Ok(new
    {
        ok = true,
        profile.Id,
        profile.Name,
        profile.Email,
        profile.HasMoodleConnected,
        hasApiKey = !string.IsNullOrWhiteSpace(profile.ApiKey),
        profile.MoodleConnections
    });
}).RequireRateLimiting(PortalAuthRateLimitPolicy);

app.MapPost("/api/account/connect-moodle", async (
    ConnectMoodleInput input,
    HttpContext context,
    IAccountService accountService,
    ConnectorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var identity = await ResolvePortalIdentityAsync(context, dbContext, cancellationToken);
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
}).RequireRateLimiting(PortalAuthRateLimitPolicy);

app.MapGet("/auth/login", (string? email, string? returnUrl) =>
{
    return Results.Content(BuildLoginPage(email, returnUrl), "text/html; charset=utf-8");
});

app.MapPost("/auth/login", async (
    HttpContext context,
    IAccountService accountService,
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
        return Results.Content(BuildLoginPage(email, returnUrl, "E-mail ou senha invalidos."), "text/html; charset=utf-8");
    }

    await SignInPortalAccountAsync(context, account.Id, account.Name, account.Email);
    return Results.Redirect(IsLocalReturnUrl(returnUrl) ? returnUrl : "/");
}).RequireRateLimiting(PortalAuthRateLimitPolicy);

app.MapGet("/auth/logout", () =>
{
    return Results.SignOut(authenticationSchemes: new[] { CookieAuthenticationDefaults.AuthenticationScheme });
});

// ─── Admin ─────────────────────────────────────────────────────────────────────

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

app.MapFallbackToFile("index.html");

app.Run();

static bool ConstantTimeEquals(string provided, string expected)
{
    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

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

    return new[] { "openid", "profile", "email", "offline_access", audienceScope.Trim() }
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

static async Task EnrichMcpPrincipalFromLocalAccountAsync(HttpContext context, CancellationToken cancellationToken)
{
    var principal = context.User;
    if (principal.Identity?.IsAuthenticated != true ||
        principal.HasClaim(claim => claim.Type == "connector_client_id"))
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

    identity.AddClaim(new Claim("connector_client_id", account.ConnectorClientId));

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

static async Task<PortalIdentity?> ResolvePortalIdentityAsync(
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
                return new PortalIdentity(accountById.Id, accountById.Name, accountById.Email);
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

                return new PortalIdentity(accountByEmail.Id, accountByEmail.Name, accountByEmail.Email);
            }
        }
    }

    return null;
}

static async Task SignInPortalAccountAsync(HttpContext context, Guid id, string name, string email)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, id.ToString()),
        new(ClaimTypes.Name, name),
        new(ClaimTypes.Email, email),
        new(OpenIddictConstants.Claims.Subject, id.ToString()),
        new(OpenIddictConstants.Claims.Name, name),
        new(OpenIddictConstants.Claims.Email, email)
    };

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
}

static string BuildLoginPage(string? email, string? returnUrl, string? error = null)
{
    var safeEmail = System.Net.WebUtility.HtmlEncode(email ?? string.Empty);
    var safeReturnUrl = System.Net.WebUtility.HtmlEncode(returnUrl ?? "/");
    var errorHtml = string.IsNullOrWhiteSpace(error)
        ? string.Empty
        : $"""<div class="error">{System.Net.WebUtility.HtmlEncode(error)}</div>""";

    return $$"""
<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Moodle Connector - Login</title>
  <style>
    body{font-family:system-ui,-apple-system,Segoe UI,sans-serif;background:#f86000;margin:0;min-height:100vh;display:grid;place-items:center;color:#fff7ed}
    main{width:min(420px,calc(100vw - 32px));background:#1f1b18;border:1px solid #3a332d;border-radius:14px;padding:28px;box-shadow:0 24px 70px rgba(0,0,0,.28)}
    h1{font-size:22px;margin:0 0 20px}
    label{display:block;font-size:12px;text-transform:uppercase;color:#d6d3d1;font-weight:700;margin:14px 0 6px}
    input{width:100%;box-sizing:border-box;border:1px solid #4a4038;border-radius:8px;background:#2a2521;color:#fff7ed;padding:11px 12px;font-size:14px}
    button{width:100%;border:0;border-radius:8px;background:#f98012;color:#1f1b18;font-weight:700;padding:11px 12px;margin-top:18px;cursor:pointer}
    .error{background:#450a0a;border:1px solid #991b1b;color:#fecaca;border-radius:8px;padding:10px 12px;font-size:14px;margin-bottom:12px}
  </style>
</head>
<body>
  <main>
    <h1>Moodle Connector</h1>
    {{errorHtml}}
    <form method="post" action="/auth/login">
      <input type="hidden" name="returnUrl" value="{{safeReturnUrl}}">
      <label for="email">E-mail</label>
      <input id="email" name="email" type="email" value="{{safeEmail}}" required autofocus>
      <label for="password">Senha</label>
      <input id="password" name="password" type="password" required>
      <button type="submit">Entrar</button>
    </form>
  </main>
</body>
</html>
""";
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
    descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

    var existing = await manager.FindByClientIdAsync(descriptor.ClientId);
    if (existing is null)
    {
        await manager.CreateAsync(descriptor);
        return;
    }

    await manager.UpdateAsync(existing, descriptor);
}

public sealed record PortalIdentity(Guid Id, string Name, string Email);

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
public sealed record ConnectMoodleInput(string MoodleAlias, string MoodleBaseUrl, string MoodleUsername, string MoodlePassword, bool IsDefault = false, bool CanWrite = false);
