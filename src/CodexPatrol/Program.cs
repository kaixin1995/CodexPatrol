using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using CodexPatrol.Api;
using CodexPatrol.Models;
using CodexPatrol.Serialization;
using CodexPatrol.Services;

var builder = WebApplication.CreateBuilder(args);

// 确保 appsettings.json 存在，缺失则生成默认配置。
EnsureAppSettingsExists();

// 统一从软件根目录读取配置，避免 dotnet run 时回退到项目根目录。
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: true);
// 关闭 ASP.NET Core 框架常规请求日志，只保留异常级别输出。
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Error);

// 手动构建配置对象，避免 AOT 不兼容的 Configuration.Bind()
var settings = new PatrolSettings();
var config = builder.Configuration.GetSection("PatrolSettings");

// 业务参数从 appsettings.json 逐项读取
settings.AutoPollingEnabled = config.GetValue("AutoPollingEnabled", false);
settings.ListenHost = NormalizeListenHost(config.GetValue("ListenHost", "0.0.0.0"));
settings.ListenPort = NormalizeListenPort(config.GetValue("ListenPort", 22014));
settings.PollIntervalMinutes = config.GetValue("PollIntervalMinutes", 10);
settings.PollRandomDelayMinMinutes = Math.Max(0, config.GetValue("PollRandomDelayMinMinutes", 1));
settings.PollRandomDelayMaxMinutes = Math.Max(settings.PollRandomDelayMinMinutes, config.GetValue("PollRandomDelayMaxMinutes", 3));
settings.ProbeWorkers = config.GetValue("ProbeWorkers", 3);
settings.ProbeBatchDelayMinMs = config.GetValue("ProbeBatchDelayMinMs", 2000);
settings.ProbeBatchDelayMaxMs = config.GetValue("ProbeBatchDelayMaxMs", 3000);
settings.ActionWorkers = config.GetValue("ActionWorkers", 4);
settings.TimeoutMs = config.GetValue("TimeoutMs", 15000);
settings.RetryCount = config.GetValue("RetryCount", 0);
settings.AutoActionMode = config.GetValue("AutoActionMode", "none")!;
settings.AutoEnableRecovered = config.GetValue("AutoEnableRecovered", false);
settings.UsedPercentThreshold = config.GetValue("UsedPercentThreshold", 95);
settings.LoginPasswordHash = config.GetValue("LoginPasswordHash", "")!;
settings.Provider = config.GetValue("Provider", "codex")!;

// 监听地址和端口统一走软件根目录配置。
builder.WebHost.UseUrls(BuildListenUrl(settings.ListenHost, settings.ListenPort));

// 敏感信息从 connection.json 读取，其次从环境变量读取
LoadConnection(settings);

builder.Services.AddSingleton(settings);
// 登录密码与启动配置统一读写软件根目录下的 appsettings.json。
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<OperationLogFileWriter>();

// 注册 CpaClient（站点配置在调用时传入，AOT 兼容）
builder.Services.AddSingleton<CpaClient>(_ =>
{
    var http = new HttpClient();
    return new CpaClient(http);
});

// 注册内存状态仓库
builder.Services.AddSingleton<RuntimeStore>(sp =>
{
    var s = sp.GetRequiredService<PatrolSettings>();
    var logFileWriter = sp.GetRequiredService<OperationLogFileWriter>();
    return new RuntimeStore(s, logFileWriter);
});

// 注册巡检引擎
builder.Services.AddSingleton<InspectionEngine>();

// 注册后台自动轮询服务
builder.Services.AddHostedService<AutoPollingService>();

// 注册 usage-queue 监控服务，后台轮询 CPA 调用队列以标记活跃账号。
builder.Services.AddHostedService<UsageQueueMonitor>();

// 配置 JSON 序列化（AOT 兼容：Source Generator 生成代码，不依赖反射）
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolver = AppJsonContext.Default;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
});

var app = builder.Build();

// 未处理异常统一写入本地异常日志，并给前端返回稳定的 500 响应。
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is not null)
        {
            var logFileWriter = context.RequestServices.GetRequiredService<OperationLogFileWriter>();
            logFileWriter.WriteException(
                exception,
                "http",
                "request",
                "system",
                message: $"未处理异常：{context.Request.Method} {context.Request.Path}{context.Request.QueryString}");
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "服务器内部错误" }, AppJsonContext.Default.ErrorResponse);
            return;
        }

        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("服务器内部错误");
    });
});

// 登录保护：未设置密码时引导到首设页面，已设置时要求先登录。
app.Use(async (context, next) =>
{
    if (IsPublicRequest(context.Request.Path))
    {
        await next();
        return;
    }

    var auth = context.RequestServices.GetRequiredService<AuthService>();
    if (!auth.HasPasswordConfigured())
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "请先设置登录密码" }, AppJsonContext.Default.ErrorResponse);
            return;
        }

        context.Response.Redirect("/setup.html");
        return;
    }

    var token = auth.GetSessionToken(context.Request);
    if (auth.IsAuthenticated(token))
    {
        await next();
        return;
    }

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new ErrorResponse { Error = "请先登录" }, AppJsonContext.Default.ErrorResponse);
        return;
    }

    context.Response.Redirect("/login.html");
});

// 静态文件（前端页面）
app.UseDefaultFiles();
app.UseStaticFiles();

// API 路由
var api = app.MapGroup("/api");

api.MapGroup("/auth").MapAuthApi();
api.MapGroup("/inspection").MapInspectionApi();
api.MapGroup("/quotas").MapQuotaApi();
api.MapGroup("/exceptions").MapExceptionApi();
api.MapGroup("/settings").MapSettingsApi();
api.MapGroup("/accounts").MapAccountApi();
api.MapGroup("/runtime").MapRuntimeApi();

// Fallback 到 index.html（SPA 支持）
app.MapFallbackToFile("index.html");

app.Run();

// 从 connection.json 或环境变量加载连接信息，直接写入 settings
static void LoadConnection(PatrolSettings settings)
{
    // 优先读 connection.json（与可执行文件同目录）
    var path = Path.Combine(AppContext.BaseDirectory, "connection.json");
    if (File.Exists(path))
    {
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var url = TryGetString(root, "CpaBaseUrl", "cpaBaseUrl");
            if (!string.IsNullOrWhiteSpace(url)) settings.CpaBaseUrl = url;

            var key = TryGetString(root, "ManagementKey", "managementKey");
            if (!string.IsNullOrWhiteSpace(key)) settings.ManagementKey = key;
        }
        catch { }
    }

    // 环境变量覆盖（CPA_BASE_URL / CPA_MANAGEMENT_KEY）
    var envUrl = Environment.GetEnvironmentVariable("CPA_BASE_URL");
    if (!string.IsNullOrWhiteSpace(envUrl)) settings.CpaBaseUrl = envUrl;

    var envKey = Environment.GetEnvironmentVariable("CPA_MANAGEMENT_KEY");
    if (!string.IsNullOrWhiteSpace(envKey)) settings.ManagementKey = envKey;
}

// 拼接监听地址字符串
static string BuildListenUrl(string host, int port)
{
    return $"http://{host}:{port}";
}

// 规范化监听主机地址，空白时回退到 0.0.0.0
static string NormalizeListenHost(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "0.0.0.0" : value.Trim();
}

// 规范化端口号，不在 1-65535 范围内时回退到默认端口 22014
static int NormalizeListenPort(int value)
{
    return value is >= 1 and <= 65535 ? value : 22014;
}

// 从 JSON 根元素中按多个候选属性名依次查找，返回第一个匹配的字符串值
static string? TryGetString(JsonElement root, params string[] names)
{
    foreach (var name in names)
    {
        if (root.TryGetProperty(name, out var value))
        {
            return value.GetString();
        }
    }

    return null;
}

// 判断请求路径是否免登录：认证接口、登录/设置页面、静态资源均放行
static bool IsPublicRequest(PathString path)
{
    if (path.StartsWithSegments("/api/auth"))
    {
        return true;
    }

    var value = path.Value ?? "";
    if (string.Equals(value, "/login.html", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "/setup.html", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    // 带扩展名的静态资源（css/js/图片等）放行，但 .html 页面不放行（需要走认证）
    return Path.HasExtension(path.Value)
        && !string.Equals(Path.GetExtension(path.Value), ".html", StringComparison.OrdinalIgnoreCase);
}

// 确保 appsettings.json 存在，缺失则写入默认配置。
static void EnsureAppSettingsExists()
{
    var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
    if (File.Exists(path))
    {
        return;
    }

    try
    {
        // 默认配置是静态内容，直接写入字符串以避免 AOT/trim 序列化告警。
        const string defaults = """
{
  "PatrolSettings": {
    "AutoPollingEnabled": false,
    "ListenHost": "0.0.0.0",
    "ListenPort": 22014,
    "PollIntervalMinutes": 10,
    "PollRandomDelayMinMinutes": 1,
    "PollRandomDelayMaxMinutes": 3,
    "ProbeWorkers": 3,
    "ProbeBatchDelayMinMs": 2000,
    "ProbeBatchDelayMaxMs": 3000,
    "ActionWorkers": 4,
    "TimeoutMs": 15000,
    "RetryCount": 0,
    "AutoActionMode": "disable",
    "AutoEnableRecovered": false,
    "UsedPercentThreshold": 95,
    "Provider": "codex"
  }
}
""";
        File.WriteAllText(path, defaults);
    }
    catch
    {
        // 写入失败不阻塞启动。
    }
}
