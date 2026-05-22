using CodexPatrol.Models;
using CodexPatrol.Services;

namespace CodexPatrol.Api;

/// <summary>
/// 登录与首设密码相关 API。
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// 注册认证相关路由。
    /// </summary>
    public static RouteGroupBuilder MapAuthApi(this RouteGroupBuilder group)
    {
        // 查询当前登录状态：是否已设密码、是否已登录。
        group.MapGet("/status", (HttpContext context, AuthService auth) =>
        {
            var configured = auth.HasPasswordConfigured();
            var authenticated = configured && auth.IsAuthenticated(auth.GetSessionToken(context.Request));
            return Results.Ok(new AuthStatusResponse
            {
                PasswordConfigured = configured,
                Authenticated = authenticated,
                SetupRequired = !configured,
            });
        });

        // 首次设置密码并自动登录。
        group.MapPost("/setup", (HttpContext context, SetupPasswordRequest request, AuthService auth) =>
        {
            // 密码已设置则不允许重复设置。
            if (auth.HasPasswordConfigured())
            {
                return Results.Conflict(new ErrorResponse { Error = "登录密码已设置，请直接登录" });
            }

            // 校验密码和确认密码是否填写完整。
            if (string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.ConfirmPassword))
            {
                return Results.BadRequest(new ErrorResponse { Error = "请完整填写密码和确认密码" });
            }

            // 校验两次输入是否一致。
            if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            {
                return Results.BadRequest(new ErrorResponse { Error = "两次输入的密码不一致" });
            }

            // 尝试设置密码，失败时返回具体错误信息。
            if (!auth.TrySetPassword(request.Password, out var error))
            {
                return Results.BadRequest(new ErrorResponse { Error = error });
            }

            // 设置成功后创建会话并写入 Cookie。
            var token = auth.CreateSessionToken();
            auth.AppendAuthCookie(context.Response, token, context.Request.IsHttps);
            return Results.Ok(new MessageResponse { Message = "登录密码设置成功" });
        });

        // 使用密码登录。
        group.MapPost("/login", (HttpContext context, LoginRequest request, AuthService auth) =>
        {
            // 尚未设置密码时要求先设置。
            if (!auth.HasPasswordConfigured())
            {
                return Results.Conflict(new ErrorResponse { Error = "请先设置登录密码" });
            }

            // 校验密码是否正确。
            if (!auth.VerifyPassword(request.Password))
            {
                return Results.Unauthorized();
            }

            // 登录成功，创建会话并写入 Cookie。
            var token = auth.CreateSessionToken();
            auth.AppendAuthCookie(context.Response, token, context.Request.IsHttps);
            return Results.Ok(new MessageResponse { Message = "登录成功" });
        });

        // 退出登录，吊销会话并清除 Cookie。
        group.MapPost("/logout", (HttpContext context, AuthService auth) =>
        {
            var token = auth.GetSessionToken(context.Request);
            auth.RevokeSession(token);
            auth.ClearAuthCookie(context.Response, context.Request.IsHttps);
            return Results.Ok(new MessageResponse { Message = "已退出登录" });
        });

        return group;
    }
}
