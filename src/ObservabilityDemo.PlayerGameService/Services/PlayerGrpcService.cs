using System.Diagnostics;
using Grpc.Core;
using Serilog;
using Serilog.Context;
using ObservabilityDemo.Shared.Constants;
using ObservabilityDemo.Shared.Protos;

namespace ObservabilityDemo.PlayerGameService.Services;

/// <summary>
/// PlayerService gRPC 實作 — 玩家登入與驗證。
/// 對應原始 Program.cs 中 SERVICE_PLAYER 的登入/驗證業務邏輯。
/// </summary>
public class PlayerGrpcService : PlayerService.PlayerServiceBase
{
    public record Dependencies(ActivitySource ActivitySource);

    private readonly ActivitySource _activitySource;

    public PlayerGrpcService(Dependencies deps)
    {
        _activitySource = deps.ActivitySource;
    }

    /// <summary>
    /// 玩家登入 (對應 Program.cs L133-165)
    /// </summary>
    public override async Task<LoginResponse> Login(LoginRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("Login", ActivityKind.Server);
        activity?.SetTag("rpc.method", "Login");
        activity?.SetTag("rpc.service", "PlayerService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "AuthenticationHandler");

        var ip = $"{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";
        var device = new[] { "iOS", "Android", "Desktop", "Mobile Web" }[Random.Shared.Next(4)];
        var browser = new[] { "Chrome", "Safari", "Firefox", "Edge" }[Random.Shared.Next(4)];
        var location = new[] { "台灣", "香港", "新加坡", "日本" }[Random.Shared.Next(4)];

        activity?.SetTag("user.ip", ip);
        activity?.SetTag("user.device", device);
        activity?.SetTag("user.location", location);

        var loginContext = new
        {
            IP = ip,
            Device = device,
            Browser = browser,
            Location = location
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.PlayerService))
        using (LogContext.PushProperty("SourceContext", "AuthenticationHandler"))
        using (LogContext.PushProperty("WorkflowStep", "1-Login"))
        using (LogContext.PushProperty("EventType", "PlayerLogin"))
        using (LogContext.PushProperty("LoginContext", loginContext, destructureObjects: true))
        {
            Log.Information("玩家登入: {UserId} from {Location} using {Device}",
                request.UserId, location, device);
        }

        await Task.Delay(100);

        return new LoginResponse
        {
            Success = true,
            Ip = ip,
            Device = device,
            Browser = browser,
            Location = location
        };
    }

    /// <summary>
    /// 玩家驗證 (對應 Program.cs L167-198)
    /// </summary>
    public override async Task<AuthenticateResponse> Authenticate(AuthenticateRequest request, ServerCallContext context)
    {
        using var activity = _activitySource.StartActivity("Authenticate", ActivityKind.Server);
        activity?.SetTag("rpc.method", "Authenticate");
        activity?.SetTag("rpc.service", "PlayerService");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("operation", "AuthorizationHandler");

        var authMethod = new[] { "Password", "Biometric", "2FA", "OAuth" }[Random.Shared.Next(4)];
        var token = Guid.NewGuid().ToString();
        var role = new[] { "Player", "VIP", "Premium" }[Random.Shared.Next(3)];

        activity?.SetTag("auth.method", authMethod);
        activity?.SetTag("user.role", role);

        var authDetails = new
        {
            AuthMethod = authMethod,
            Token = token,
            Role = role,
            AuthTimestamp = DateTime.UtcNow
        };

        using (LogContext.PushProperty("ServiceName", ServiceNames.PlayerService))
        using (LogContext.PushProperty("SourceContext", "AuthorizationHandler"))
        using (LogContext.PushProperty("WorkflowStep", "2-Authentication"))
        using (LogContext.PushProperty("EventType", "PlayerAuthenticated"))
        using (LogContext.PushProperty("AuthDetails", authDetails, destructureObjects: true))
        {
            Log.Information("玩家驗證成功: {UserId} with {AuthMethod}, Role: {Role}",
                request.UserId, authMethod, role);
        }

        await Task.Delay(100);

        return new AuthenticateResponse
        {
            Success = true,
            AuthMethod = authMethod,
            Token = token,
            Role = role
        };
    }
}
