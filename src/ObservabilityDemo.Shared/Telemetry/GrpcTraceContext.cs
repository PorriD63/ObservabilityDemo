using System.Diagnostics;
using Grpc.Core;

namespace ObservabilityDemo.Shared.Telemetry;

/// <summary>
/// gRPC server 端 trace context 工具。
/// ASP.NET Core 接收 gRPC 請求時會自動建立中間 Activity，
/// 但在多 TracerProvider 架構下不會被匯出，導致手動建立的 span 成為 orphan。
/// 此擴充方法從 gRPC metadata 直接解析 traceparent，並暫時清除 Activity.Current
/// 以確保 StartActivity 使用明確指定的 parentContext，而非被 ASP.NET Core Activity 覆蓋。
/// </summary>
public static class GrpcTraceContext
{
    /// <summary>
    /// 在 gRPC server 端建立 Activity，從 ServerCallContext 的 traceparent header
    /// 直接解析遠端 CLIENT span，繞過 ASP.NET Core 未匯出的中間 Activity。
    /// </summary>
    public static Activity? StartGrpcServerActivity(
        this ActivitySource source, string name, ServerCallContext context)
    {
        var traceparent = context.RequestHeaders.GetValue("traceparent");
        var parentContext = ExtractTraceParent(traceparent);

        if (parentContext.HasValue)
        {
            // 暫時清除 Activity.Current，避免 Activity.Start() 用 ASP.NET Core 的
            // 中間 Activity 覆蓋我們指定的 parentContext。
            // StartActivity 會自動將新建的 Activity 設為 Activity.Current。
            var previous = Activity.Current;
            Activity.Current = null;
            var activity = source.StartActivity(name, ActivityKind.Server, parentContext.Value);
            if (activity == null)
            {
                // 若 ActivitySource 沒有 listener，還原 Activity.Current
                Activity.Current = previous;
            }
            return activity;
        }

        return source.StartActivity(name, ActivityKind.Server);
    }

    private static ActivityContext? ExtractTraceParent(string? traceparent)
    {
        if (string.IsNullOrEmpty(traceparent)) return null;

        var parts = traceparent.Split('-');
        if (parts.Length < 4) return null;

        var traceId = ActivityTraceId.CreateFromString(parts[1]);
        var spanId = ActivitySpanId.CreateFromString(parts[2]);
        var flags = parts[3] == "01" ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;

        return new ActivityContext(traceId, spanId, flags, isRemote: true);
    }
}
