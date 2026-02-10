# PLAN.md — 拆分微服務架構 + Kafka Event-Driven + gRPC

## Context

目前 `dotnet-demo/Program.cs` 是單一 process 模擬 7 個微服務，所有 trace、log 都在同一 process 內偽造。目標是拆成 4 個專案群組 (混合模式)，服務間使用 **gRPC** 做同步通訊，搭配 **Kafka** 做非同步 event-driven，讓可觀測性 demo 更真實。

---

## Architecture

### 4 個專案群組

| 專案 | 包含服務 | 角色 |
|------|---------|------|
| **GatewayService** | ApiGateway | Workflow 編排器，透過 gRPC 呼叫下游 |
| **PlayerGameService** | PlayerService + GameService | gRPC server，玩家登入/驗證 + 遊戲操作 |
| **FinanceService** | WalletService + PaymentService + RiskService | gRPC server，錢包/支付/風控 + Kafka producer |
| **NotificationService** | NotificationService | Worker SDK，純 Kafka consumer |

### 通訊模式

**gRPC (同步):**
- Gateway → PlayerGameService: 登入、驗證、遊戲開始、下注、遊戲結果
- Gateway → FinanceService: 存款、提款、結算、餘額更新
- PlayerGameService → FinanceService: 餘額查詢

**Kafka (非同步):**
- `bet-settled` → NotificationService
- `payment-processed` → NotificationService
- `withdrawal-approved` / `withdrawal-flagged` → NotificationService
- `workflow-completed` → NotificationService
- `insufficient-balance` → NotificationService

### 目錄結構

```
SeqDemo/
├── SeqDemo.sln
├── protos/                            # 共享 .proto 定義
│   ├── player_game.proto              # PlayerGameService RPC 定義
│   └── finance.proto                  # FinanceService RPC 定義
├── src/
│   ├── SeqDemo.Shared/                # 共享：OTel 設定、Kafka 工具、Event DTOs、常數
│   ├── SeqDemo.GatewayService/        # Port 5100, BackgroundService + gRPC client
│   ├── SeqDemo.PlayerGameService/     # Port 5200, gRPC server
│   ├── SeqDemo.FinanceService/        # Port 5300, gRPC server + Kafka producer
│   └── SeqDemo.NotificationService/   # Worker SDK, 純 Kafka consumer
├── dotnet-demo/                       # 保留原始版本作為參考
├── config/                            # 不變
└── docker-compose.yml                 # 新增 Kafka (KRaft) + Kafka UI
```

### Proto 定義概覽

**`protos/player_game.proto`:**
```protobuf
service PlayerService {
  rpc Login (LoginRequest) returns (LoginResponse);
  rpc Authenticate (AuthenticateRequest) returns (AuthenticateResponse);
}

service GameService {
  rpc StartGame (StartGameRequest) returns (StartGameResponse);
  rpc PlaceBet (PlaceBetRequest) returns (PlaceBetResponse);
  rpc GetGameResult (GetGameResultRequest) returns (GetGameResultResponse);
}
```

**`protos/finance.proto`:**
```protobuf
service WalletService {
  rpc GetBalance (GetBalanceRequest) returns (GetBalanceResponse);
  rpc Settlement (SettlementRequest) returns (SettlementResponse);
  rpc UpdateBalance (UpdateBalanceRequest) returns (UpdateBalanceResponse);
  rpc InitiateDeposit (InitiateDepositRequest) returns (InitiateDepositResponse);
  rpc CreditBalance (CreditBalanceRequest) returns (CreditBalanceResponse);
  rpc RequestWithdrawal (RequestWithdrawalRequest) returns (RequestWithdrawalResponse);
}

service PaymentService {
  rpc ValidatePayment (ValidatePaymentRequest) returns (ValidatePaymentResponse);
  rpc ProcessPayment (ProcessPaymentRequest) returns (ProcessPaymentResponse);
  rpc ApproveWithdrawal (ApproveWithdrawalRequest) returns (ApproveWithdrawalResponse);
}

service RiskService {
  rpc AssessRisk (AssessRiskRequest) returns (AssessRiskResponse);
}
```

---

## 實作 Checklist

### Phase 1: Proto 定義 + 共享函式庫 (SeqDemo.Shared)

**Proto 檔案：**
- [x] `protos/player_game.proto` — PlayerService、GameService 的 RPC 定義 (含 request/response messages)
- [x] `protos/finance.proto` — WalletService、PaymentService、RiskService 的 RPC 定義

**共享函式庫 `src/SeqDemo.Shared/`：**
- [x] 建立 `SeqDemo.Shared.csproj` (含所有共用套件參照)
- [x] `Constants/ServiceNames.cs` — 7 個微服務名稱常數
- [x] `Constants/KafkaTopics.cs` — 6 個 Kafka topic 名稱常數
- [x] `Telemetry/ActivityEnricher.cs` — 從 Program.cs 搬出，自動注入 TraceId/SpanId 到 log
- [x] `Telemetry/TelemetrySetup.cs` — 集中 Serilog + TracerProvider 設定（支援 gRPC server/client instrumentation）
- [x] `Kafka/KafkaProducer.cs` — Produce + 注入 W3C traceparent 到 Kafka headers
- [x] `Kafka/KafkaConsumer.cs` — Consume + 從 headers 提取 traceparent，用 parentContext 接續同一 Trace
- [x] `Events/BetSettledEvent.cs` — record DTO
- [x] `Events/PaymentProcessedEvent.cs` — record DTO
- [x] `Events/WithdrawalApprovedEvent.cs` — record DTO
- [x] `Events/WithdrawalFlaggedEvent.cs` — record DTO
- [x] `Events/WorkflowCompletedEvent.cs` — record DTO
- [x] `Events/InsufficientBalanceEvent.cs` — record DTO
- [x] 確認 `dotnet build` 通過

**套件（實際安裝版本）：**
- Serilog 4.3, Serilog.Sinks.Console 6.1.1, Serilog.Sinks.OpenTelemetry 4.2
- OpenTelemetry 1.15.0, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.0
- OpenTelemetry.Extensions.Hosting 1.15.0
- **Grpc.Net.Client 2.76.0** (client 端)
- **Google.Protobuf 3.33.5**, **Grpc.Tools 2.78.0** (proto 編譯)
- **OpenTelemetry.Instrumentation.GrpcNetClient 1.12.0-beta.1** (自動傳播 traceparent 到 gRPC calls)
- **OpenTelemetry.Instrumentation.AspNetCore 1.15.0** (自動建立 server span for incoming gRPC)
- **Confluent.Kafka 2.13.0**

> **gRPC 選型理由：** gRPC 使用 HTTP/2，原生支援 metadata 傳播。OTel.Instrumentation.GrpcNetClient + AspNetCore 自動注入/提取 W3C traceparent，trace context 傳播零配置。在 Tempo service graph 中會顯示為 `rpc.method` + `rpc.service` 屬性。

> **Confluent.Kafka 選型理由：** 直接操作 Kafka client 可明確展示 trace context 如何在 message headers 中傳播，更具教育意義。

---

### Phase 2: NotificationService + Docker Compose (最簡單，先做)

**Docker Compose：**
- [x] 新增 Kafka (KRaft mode, `confluentinc/cp-kafka:7.8.0`) 到 `docker-compose.yml`
- [x] 新增 Kafka UI (`provectuslabs/kafka-ui:latest`) 到 `docker-compose.yml`
- [ ] 確認 `docker compose up -d` 啟動 Kafka 成功

**NotificationService `src/SeqDemo.NotificationService/`：**
- [x] 建立 `SeqDemo.NotificationService.csproj` (Worker SDK + 參照 SeqDemo.Shared)
- [x] `Program.cs` — Host 設定、Serilog、TracerProvider 初始化
- [x] `Workers/NotificationWorker.cs` — BackgroundService 消費所有 Kafka topics
- [x] 從 Kafka headers 提取 traceparent → 用 `parentContext` 建立 Consumer span (同 TraceId)
- [x] 每個 topic 對應不同的通知 log 訊息 (對應 Program.cs 中 `SERVICE_NOTIFICATION` 區塊)
- [x] 確認 `dotnet build` 通過
- [ ] 確認能成功連線 Kafka 並消費訊息

---

### Phase 3: FinanceService

**gRPC server `src/SeqDemo.FinanceService/`：**
- [x] 建立 `SeqDemo.FinanceService.csproj` (Grpc.AspNetCore + 參照 SeqDemo.Shared + proto)
- [x] `Program.cs` — Host 設定、Serilog、TracerProvider、gRPC server 註冊 (Port 5300)
- [x] `Services/WalletGrpcService.cs` — 實作 `WalletService` gRPC service
  - [x] `GetBalance` — 餘額查詢 (對應 Program.cs L200-242)
  - [x] `Settlement` — 注單結算 (對應 Program.cs L440-544)
  - [x] `UpdateBalance` — 餘額更新 (對應 Program.cs L497-544)
  - [x] `InitiateDeposit` — 發起存款 (對應 Program.cs L596-646)
  - [x] `CreditBalance` — 餘額入帳 (對應 Program.cs L773-805)
  - [x] `RequestWithdrawal` — 提款請求 (對應 Program.cs L858-996)
- [x] `Services/PaymentGrpcService.cs` — 實作 `PaymentService` gRPC service
  - [x] `ValidatePayment` — 驗證支付方式 (對應 Program.cs L648-702)
  - [x] `ProcessPayment` — 處理支付 (對應 Program.cs L704-805)
  - [x] `ApproveWithdrawal` — 核准提款 (對應 Program.cs L1077-1107)
- [x] `Services/RiskGrpcService.cs` — 實作 `RiskService` gRPC service
  - [x] `AssessRisk` — 風險評估 (對應 Program.cs L998-1070)
- [x] Wallet→Payment→Risk process 內呼叫保留 ActivitySource CLIENT/SERVER span 配對
- [x] 結算/支付/提款後透過 KafkaProducer publish 對應 events
- [x] 保留所有機率性錯誤/警告情境
- [x] 確認 `dotnet build` 通過

---

### Phase 4: PlayerGameService

**gRPC server `src/SeqDemo.PlayerGameService/`：**
- [x] 建立 `SeqDemo.PlayerGameService.csproj` (Grpc.AspNetCore + Grpc.Net.Client + 參照 SeqDemo.Shared + proto)
- [x] `Program.cs` — Host 設定、Serilog、兩個 TracerProvider (PlayerService, GameService)、gRPC server 註冊 (Port 5200)
- [x] `Services/PlayerGrpcService.cs` — 實作 `PlayerService` gRPC service
  - [x] `Login` — 玩家登入 (對應 Program.cs L133-165)
  - [x] `Authenticate` — 玩家驗證 (對應 Program.cs L167-198)
- [x] `Services/GameGrpcService.cs` — 實作 `GameService` gRPC service
  - [x] `StartGame` — 遊戲開始 (對應 Program.cs L244-303)
  - [x] `PlaceBet` — 下注，含餘額檢查 (對應 Program.cs L305-406)
  - [x] `GetGameResult` — 遊戲結果 (對應 Program.cs L408-438)
- [x] 餘額查詢透過 gRPC client 呼叫 FinanceService (`WalletService/GetBalance`)
- [x] 維持兩個 ActivitySource (PlayerService, GameService) → Tempo service graph 顯示兩個節點
- [x] 保留所有機率性錯誤/警告情境 (連線延遲、下注限額等)
- [x] 確認 `dotnet build` 通過

---

### Phase 5: GatewayService

**BackgroundService + gRPC client `src/SeqDemo.GatewayService/`：**
- [x] 建立 `SeqDemo.GatewayService.csproj` (Worker SDK + Grpc.Net.Client + 參照 SeqDemo.Shared + proto)
- [x] `Program.cs` — Host 設定、Serilog、TracerProvider (ApiGateway)、gRPC client 註冊
- [x] `Workers/BettingWorkflowWorker.cs` — BackgroundService，透過 gRPC client 編排 8 步驟
  - [x] 步驟 1-2: gRPC → PlayerGameService (Login, Authenticate)
  - [x] 步驟 3: gRPC → FinanceService (GetBalance)
  - [x] 步驟 4-6: gRPC → PlayerGameService (StartGame, PlaceBet, GetGameResult)
  - [x] 步驟 7-8: gRPC → FinanceService (Settlement, UpdateBalance)
  - [x] Workflow 完成後 Kafka publish `workflow-completed`
  - [x] 餘額不足時 Kafka publish `insufficient-balance`
- [x] `Workers/DepositWorkflowWorker.cs` — BackgroundService，透過 gRPC client 編排 4 步驟
  - [x] 步驟 1: gRPC → FinanceService (InitiateDeposit)
  - [x] 步驟 2-3: gRPC → FinanceService (ValidatePayment, ProcessPayment)
  - [x] 步驟 4: gRPC → FinanceService (CreditBalance)
  - [x] Workflow 完成後 Kafka publish `workflow-completed`
- [x] `Workers/WithdrawalWorkflowWorker.cs` — BackgroundService，透過 gRPC client 編排 3 步驟
  - [x] 步驟 1: gRPC → FinanceService (RequestWithdrawal)
  - [x] 步驟 2: gRPC → FinanceService (AssessRisk)
  - [x] 步驟 3: gRPC → FinanceService (ApproveWithdrawal 或 FlagReview)
  - [x] Workflow 完成後 Kafka publish `workflow-completed`
- [x] OTel.Instrumentation.GrpcNetClient 自動傳播 traceparent → 真正分散式 trace
- [x] 每個 workflow 以 root span (ActivityKind.Server) 開始 → 產生 TraceId
- [x] 確認 `dotnet build` 通過

---

### Phase 6: 整合與文件

**解決方案：**
- [ ] 建立 `SeqDemo.sln`，加入所有 5 個專案
- [ ] 確認 `dotnet build SeqDemo.sln` 全部通過

**啟動腳本：**
- [ ] 更新 `start-all.bat` — 啟動 docker compose + 依序啟動 4 個服務
- [ ] 更新 `start-all.sh` — 同上

**文件：**
- [ ] 更新 `CLAUDE.md` — 反映新架構、新目錄結構、新服務埠號
- [ ] 更新 `README.md` — 反映新架構

---

### 端對端驗證

- [ ] `docker compose up -d` 啟動基礎設施 (含 Kafka)
- [ ] 依序啟動: FinanceService → PlayerGameService → GatewayService → NotificationService
- [ ] **gRPC 連通性**: Gateway 能成功呼叫 PlayerGameService 和 FinanceService
- [ ] **Kafka 連通性**: NotificationService 能消費到 events
- [ ] **TraceId 一致性**: 在 Tempo 查詢單一 trace，確認 span 鏈跨越所有 4 個服務
- [ ] **Service Graph**: Tempo service graph 顯示 Gateway→PlayerGame→Finance 的 gRPC edges
- [ ] **Kafka span 鏈**: trace 中能看到 PRODUCER→CONSUMER span 且 TraceId 一致
- [ ] **Log 關聯**: 在 Seq/Loki 用 TraceId 過濾，能看到所有服務的 log
- [ ] **service.name 區分**: 各服務的 log 有獨立的 service.name
- [ ] **Kafka UI**: localhost:8080 能看到 topic 列表和訊息內容
- [ ] **錯誤情境**: 餘額不足、支付驗證失敗等情境仍正常觸發並產生對應 log/span status

---

## Docker Compose 新增

```yaml
kafka:
  image: confluentinc/cp-kafka:7.8.0
  # KRaft mode (無需 Zookeeper)
  ports: ["9092:9092"]

kafka-ui:
  image: provectuslabs/kafka-ui:latest
  ports: ["8080:8080"]
```

OTel Collector / Tempo / Prometheus 設定**不需修改**，因為各服務仍透過 OTLP gRPC → 4317 送資料，只是 service.name 變成真實不同的值。

---

## 服務埠號

| 服務 | 埠號 | 協定 |
|------|------|------|
| GatewayService | 5100 | gRPC client only (無需對外) |
| PlayerGameService | 5200 | gRPC server |
| FinanceService | 5300 | gRPC server |
| NotificationService | — | Kafka consumer only (無需對外) |
| Kafka | 9092 | Kafka protocol |
| Kafka UI | 8080 | HTTP |

---

## TraceId 端對端傳遞 — 核心設計

### 目標

**同一個 Workflow 的所有操作（跨 4 個 process、跨 gRPC + Kafka）共用同一個 TraceId**，在 Tempo/Seq 中查詢一個 TraceId 即可看到完整的 span 鏈。

### BettingWorkflow 完整 Trace 範例

```
TraceId: abcd1234...（整個 workflow 共用）

GatewayService
 └─ [SERVER] BettingWorkflow                          ← root span, 產生 TraceId
     ├─ [CLIENT] grpc PlayerService/Login             ← OTel auto-instrumentation
     │   └─ PlayerGameService
     │       └─ [SERVER] PlayerService/Login           ← 同 TraceId (gRPC metadata 自動帶入)
     │
     ├─ [CLIENT] grpc PlayerService/Authenticate
     │   └─ PlayerGameService
     │       └─ [SERVER] PlayerService/Authenticate
     │
     ├─ [CLIENT] grpc WalletService/GetBalance
     │   └─ FinanceService
     │       └─ [SERVER] WalletService/GetBalance      ← 同 TraceId
     │
     ├─ [CLIENT] grpc GameService/StartGame
     │   └─ PlayerGameService
     │       └─ [SERVER] GameService/StartGame
     │           ├─ [CLIENT] grpc WalletService/GetBalance  ← 跨第三個 process
     │           │   └─ FinanceService
     │           │       └─ [SERVER] WalletService/GetBalance
     │           └─ ...PlaceBet, GetGameResult
     │
     ├─ [CLIENT] grpc WalletService/Settlement
     │   └─ FinanceService
     │       └─ [SERVER] WalletService/Settlement
     │           └─ [PRODUCER] kafka: bet-settled      ← 同 TraceId 注入 Kafka header
     │
     └─ [PRODUCER] kafka: workflow-completed
         └─ NotificationService
             └─ [CONSUMER] kafka: workflow-completed    ← 同 TraceId (從 header 提取，接續 trace)
```

### 傳播路徑 1: gRPC (全自動)

```
GatewayService                          PlayerGameService / FinanceService
┌──────────────────┐                    ┌──────────────────┐
│ Activity.Current │                    │                  │
│ TraceId: abcd... │                    │                  │
│                  │  gRPC metadata     │                  │
│ GrpcNetClient    │───────────────────▶│ AspNetCore       │
│ Instrumentation  │  traceparent:      │ Instrumentation  │
│ (自動注入)        │  00-abcd...-xx-01 │ (自動提取)        │
│                  │                    │                  │
│                  │                    │ Activity.Current │
│                  │                    │ TraceId: abcd... │ ← 同一個 TraceId
└──────────────────┘                    └──────────────────┘
```

**零配置：** `AddGrpcClientInstrumentation()` + `AddAspNetCoreInstrumentation()` 自動處理。
gRPC client 發送時將 `Activity.Current` 的 traceparent 寫入 gRPC metadata；
gRPC server 收到時從 metadata 提取 traceparent，建立子 span（ParentId 指向 client span）。

### 傳播路徑 2: Kafka (手動注入/提取，接續同一 Trace)

```
FinanceService (Producer)                NotificationService (Consumer)
┌──────────────────────┐                 ┌──────────────────────────┐
│ Activity.Current     │                 │                          │
│ TraceId: abcd...     │                 │                          │
│ SpanId:  5678...     │                 │                          │
│                      │  Kafka Headers  │                          │
│ KafkaProducer:       │────────────────▶│ KafkaConsumer:           │
│  1. 建立 PRODUCER    │  traceparent:   │  1. 提取 traceparent     │
│     span             │  00-abcd...-    │  2. 用 ActivityContext    │
│  2. 注入 traceparent │  9abc...-01     │     還原為 parent context│
│     到 message header│                 │  3. StartActivity 建立   │
│                      │                 │     CONSUMER span        │
│                      │                 │     ParentId = 9abc...   │
│                      │                 │     TraceId  = abcd...   │ ← 同一個 TraceId!
└──────────────────────┘                 └──────────────────────────┘
```

**關鍵實作 — KafkaProducer (注入):**
```csharp
// 在 produce 前，將當前 Activity 的 context 注入到 Kafka message headers
var activity = activitySource.StartActivity("kafka.produce", ActivityKind.Producer);
activity?.SetTag("messaging.system", "kafka");
activity?.SetTag("messaging.destination.name", topic);

// 注入 W3C traceparent 到 Kafka headers
var headers = new Headers();
if (activity != null)
{
    var traceParent = $"00-{activity.TraceId}-{activity.SpanId}-{(activity.Recorded ? "01" : "00")}";
    headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));
}
```

**關鍵實作 — KafkaConsumer (提取並接續):**
```csharp
// 從 Kafka message headers 提取 traceparent
var traceparentBytes = consumeResult.Message.Headers
    .FirstOrDefault(h => h.Key == "traceparent")?.GetValueBytes();

if (traceparentBytes != null)
{
    var traceparent = Encoding.UTF8.GetString(traceparentBytes);
    // 解析: "00-{traceId}-{parentSpanId}-{flags}"
    var parts = traceparent.Split('-');
    var traceId = ActivityTraceId.CreateFromString(parts[1]);
    var spanId = ActivitySpanId.CreateFromString(parts[2]);

    // 用 parentContext 建立 consumer span → 同一個 TraceId，ParentId 指向 producer span
    var parentContext = new ActivityContext(traceId, spanId, ActivityTraceFlags.Recorded, isRemote: true);
    using var activity = activitySource.StartActivity("kafka.consume", ActivityKind.Consumer,
        parentContext: parentContext);  // ← 關鍵：parentContext 而非 links
    // ...
}
```

> **為什麼用 `parentContext` 而非 `links`?**
> - `parentContext`: Consumer span 成為 Producer span 的子 span，**共用同一個 TraceId**。在 Tempo 查一個 TraceId 就能看到 Gateway → Finance → Kafka → Notification 完整鏈路。
> - `links`: Consumer span 產生新的 TraceId，只透過 link 關聯。需要跳轉兩個 trace 才能看全貌，不利於 demo 展示。

### 傳播路徑圖 (完整)

```
GatewayService (BackgroundService)
    │
    ├──▶ gRPC call ──▶ PlayerGameService (Port 5200)
    │    [traceparent 自動透過 gRPC metadata 傳播]
    │    [OTel GrpcNetClient → AspNetCore instrumentation]
    │         │
    │         └──▶ gRPC call ──▶ FinanceService (Port 5300)
    │              [traceparent 自動透過 gRPC metadata 傳播]
    │
    ├──▶ gRPC call ──▶ FinanceService (Port 5300)
    │    [traceparent 自動透過 gRPC metadata 傳播]
    │         │
    │         └──▶ Kafka produce (bet-settled / payment-processed / ...)
    │              [traceparent 手動注入 Kafka message headers]
    │                   │
    │                   └──▶ NotificationService (Kafka consumer)
    │                        [traceparent 手動提取，用 parentContext 接續同一 Trace]
    │
    └──▶ Kafka produce (workflow-completed)
         [traceparent 手動注入 Kafka message headers]
              │
              └──▶ NotificationService
                   [同一個 TraceId, 完整鏈路可追蹤]
```

### Log 與 Trace 關聯

每個服務的 Serilog 都配置 `ActivityEnricher`，自動將 `Activity.Current.TraceId` 寫入每筆 log。
因為 gRPC/Kafka 傳播保證了同一個 TraceId，所以：

```
在 Seq/Loki 查詢: TraceId = "abcd1234..."
→ 同時看到 GatewayService、PlayerGameService、FinanceService、NotificationService 的 log
→ 每筆 log 的 TraceId 完全一致，可排序重建完整業務流程
```

| 欄位 | 來源 | 說明 |
|------|------|------|
| `TraceId` | `Activity.Current.TraceId` (ActivityEnricher 自動注入) | 跨所有服務一致 |
| `SpanId` | `Activity.Current.SpanId` | 每個操作獨立 |
| `ParentSpanId` | `Activity.Current.ParentSpanId` | 建立 span 層級關係 |
| `service.name` | TracerProvider resource | 區分來源服務 |
| `WorkflowName` | LogContext push | 區分 Betting/Deposit/Withdrawal |

---

## 可觀測性改善

| 改善項目 | Before | After |
|---------|--------|-------|
| 分散式追蹤 | 同 process 內偽造 | 真正跨 process gRPC + Kafka trace |
| Service Graph | 靠多個 TracerProvider 模擬 | 真實 gRPC edges (`rpc.system=grpc`) |
| 非同步追蹤 | 無 | Kafka Producer→Consumer span 鏈 |
| 日誌 service.name | 全部 "DotnetSeqDemo" | 每個服務獨立 service.name |
| Span 屬性 | 手動 `http.method/url` | 自動 `rpc.system`, `rpc.service`, `rpc.method` |

---

## 關鍵檔案參考

- `dotnet-demo/Program.cs` — 所有業務邏輯來源 (1178 行)
- `dotnet-demo/DotnetSeqDemo.csproj` — 套件版本基準
- `docker-compose.yml` — 基礎設施定義
- `config/otel-collector/config.yaml` — 不需改但要理解
- `config/tempo/tempo-config.yaml` — service-graphs 已啟用

---

## 驗證方式

1. `docker compose up -d` 啟動基礎設施 (含 Kafka)
2. 依序啟動 4 個服務 (`dotnet run`)
3. 在 Tempo (Grafana) 確認 service graph 顯示跨服務 edges，span 包含 `rpc.system=grpc`
4. 在 Seq/Loki 確認不同 service.name 的日誌
5. 在 Kafka UI (localhost:8080) 確認 topic 有訊息
6. 點選任一 trace 應能看到完整的 gRPC + Kafka span 鏈
