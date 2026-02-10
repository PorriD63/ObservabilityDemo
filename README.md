# 遊戲平台可觀測性 Demo

此專案使用 4 個獨立 .NET 微服務，透過 **gRPC** (同步) 和 **Kafka** (非同步) 通訊，模擬遊戲平台的完整業務流程，產生真實的分散式追蹤與結構化日誌，並透過完整的可觀測性（Observability）基礎設施進行收集與視覺化。

## 架構概覽

### 微服務

| 專案 | 包含服務 | 角色 | 埠號 |
|------|---------|------|------|
| **GatewayService** | ApiGateway | Workflow 編排器，透過 gRPC 呼叫下游 | 5100 |
| **PlayerGameService** | PlayerService + GameService | gRPC server，玩家登入/驗證 + 遊戲操作 | 5200 |
| **FinanceService** | WalletService + PaymentService + RiskService | gRPC server，錢包/支付/風控 + Kafka producer | 5300 |
| **NotificationService** | NotificationService | Worker SDK，純 Kafka consumer | — |

### 通訊模式

```
GatewayService ──gRPC──▶ PlayerGameService ──gRPC──▶ FinanceService
      │                                                    │
      │                                              Kafka produce
      │                                                    │
      └──────── Kafka produce ──────────────▶ NotificationService (Kafka consume)
```

**gRPC (同步):** Gateway 呼叫 PlayerGameService 和 FinanceService 編排工作流程
**Kafka (非同步):** FinanceService/GatewayService 發布事件，NotificationService 消費並處理通知

### TraceId 端對端傳播

同一個 Workflow 的所有操作（跨 4 個 process、跨 gRPC + Kafka）共用同一個 TraceId：
- **gRPC**: OTel Instrumentation 自動傳播 W3C traceparent（零配置）
- **Kafka**: 手動注入/提取 traceparent 到 message headers，用 `parentContext` 接續同一 Trace

## 專案結構

```
SeqDemo/
├── SeqDemo.sln
├── protos/                            # 共享 .proto 定義
│   ├── player_game.proto
│   └── finance.proto
├── src/
│   ├── SeqDemo.Shared/                # 共享：OTel 設定、Kafka 工具、Event DTOs、常數
│   ├── SeqDemo.GatewayService/        # Port 5100, BackgroundService + gRPC client
│   ├── SeqDemo.PlayerGameService/     # Port 5200, gRPC server
│   ├── SeqDemo.FinanceService/        # Port 5300, gRPC server + Kafka producer
│   └── SeqDemo.NotificationService/   # Worker SDK, 純 Kafka consumer
├── dotnet-demo/                       # 保留原始單體版本作為參考
├── config/                            # 各服務設定檔
│   ├── otel-collector/                # OpenTelemetry Collector
│   ├── grafana/                       # Grafana Dashboards & Datasources
│   ├── prometheus/                    # Prometheus
│   ├── tempo/                         # Tempo (分散式追蹤)
│   ├── loki/                          # Loki (日誌聚合)
│   └── elasticsearch/                 # Elasticsearch Index Template
├── docker-compose.yml                 # 可觀測性基礎設施 + Kafka
├── start-all.bat                      # Windows 快速啟動腳本
└── start-all.sh                       # Linux/Mac 快速啟動腳本
```

## 可觀測性基礎設施

透過 `docker-compose.yml` 啟動以下服務：

| 服務 | 端口 | 用途 |
|---|---|---|
| **OpenTelemetry Collector** | 4317 (gRPC), 4318 (HTTP) | 接收應用程式的 Traces、Logs、Metrics 並轉發 |
| **Seq** | 5341 | 結構化日誌查詢與分析 |
| **Grafana** | 3000 | 統一視覺化儀表板（Traces / Logs / Metrics） |
| **Tempo** | 3200 | 分散式追蹤後端 |
| **Loki** | 3100 | 日誌聚合 |
| **Prometheus** | 9090 | Metrics 儲存與查詢 |
| **Elasticsearch** | 9200 | 日誌儲存與搜尋 |
| **Kibana** | 5601 | Elasticsearch 視覺化 |
| **Kafka** | 9092 | 非同步訊息佇列 (KRaft mode) |
| **Kafka UI** | 8080 | Kafka 管理介面 |

資料流向：

```
4 個微服務 ──OTLP/gRPC──▶ OTel Collector ──▶ Seq (Logs)
                                           ──▶ Loki (Logs)
                                           ──▶ Tempo (Traces)
                                           ──▶ Prometheus (Metrics)
                                           ──▶ Elasticsearch (Logs)
```

## 快速開始

### 1. 啟動可觀測性基礎設施

```bash
docker compose up -d
```

等待約 30 秒讓所有服務啟動完成。

### 2. 建置解決方案

```bash
dotnet build SeqDemo.sln
```

### 3. 依序啟動微服務

在不同終端依序啟動（gRPC server 需先於 client 啟動）：

```bash
# 終端 1: FinanceService (gRPC server, port 5300)
cd src/SeqDemo.FinanceService && dotnet run

# 終端 2: PlayerGameService (gRPC server, port 5200)
cd src/SeqDemo.PlayerGameService && dotnet run

# 終端 3: GatewayService (workflow orchestrator, port 5100)
cd src/SeqDemo.GatewayService && dotnet run

# 終端 4: NotificationService (Kafka consumer)
cd src/SeqDemo.NotificationService && dotnet run
```

或使用一鍵啟動腳本：

```bash
# Windows
start-all.bat

# Linux / Mac
./start-all.sh
```

### 4. 查看日誌與追蹤

- **Seq**: http://localhost:5341 — 結構化日誌查詢
- **Grafana**: http://localhost:3000 — 儀表板（帳號：admin / admin）
- **Kibana**: http://localhost:5601 — Elasticsearch 日誌
- **Prometheus**: http://localhost:9090 — Metrics 查詢
- **Kafka UI**: http://localhost:8080 — Kafka topic 與訊息管理

## 模擬的遊戲平台 Workflow

應用程式會隨機產生以下三種業務流程，每個 Workflow 包含多個步驟，並使用 `TraceId`、`CorrelationId`、`UserId` 串聯所有相關日誌。

### Workflow 1: 下注流程 (BettingWorkflow)

完整的 8 步驟下注流程：

1. **玩家登入** (`WorkflowStep: 1-Login`)
   - 包含 `LoginContext` 物件：IP、設備、瀏覽器、地區
2. **玩家驗證** (`WorkflowStep: 2-Authentication`)
   - 包含 `AuthDetails` 物件：驗證方式、Token、角色
3. **查詢餘額** (`WorkflowStep: 3-BalanceCheck`)
   - 包含 `BalanceInfo` 物件：餘額、貨幣、錢包 ID
4. **遊戲開始** (`WorkflowStep: 4-GameStart`)
   - 包含 `GameDetails` 物件：遊戲類型、桌台、荷官、下注範圍
5. **下注** (`WorkflowStep: 5-PlaceBet`)
   - 包含 `BetDetails` 物件：金額、貨幣、下注選項、剩餘餘額
6. **遊戲結果** (`WorkflowStep: 6-GameResult`)
   - 包含 `ResultDetails` 物件：結果、牌面、遊戲回合
7. **注單結算** (`WorkflowStep: 7-Settlement`)
   - 包含 `SettlementDetails` 物件：下注金額、贏得金額、利潤、狀態
8. **餘額更新** (`WorkflowStep: 8-BalanceUpdate`)
   - 包含 `BalanceChange` 物件：前後餘額、變更金額、變更類型

### Workflow 2: 存款流程 (DepositWorkflow)

完整的 4 步驟存款流程：

1. **發起存款** (`WorkflowStep: 1-InitiateDeposit`)
   - 包含 `DepositRequest` 物件：金額、支付方式、請求時間
2. **驗證支付方式** (`WorkflowStep: 2-ValidatePayment`)
   - 包含 `ValidationDetails` 物件：驗證規則、處理器 ID
3. **處理支付** (`WorkflowStep: 3-ProcessPayment`)
   - 包含 `PaymentDetails` 物件：交易 ID、狀態、手續費
4. **餘額入帳** (`WorkflowStep: 4-CreditBalance`)
   - 包含 `CreditDetails` 物件：金額、交易 ID、入帳後餘額

### Workflow 3: 提款流程 (WithdrawalWorkflow)

完整的 3 步驟提款流程：

1. **提款請求** (`WorkflowStep: 1-RequestWithdrawal`)
   - 包含 `WithdrawalRequest` 物件：金額、提款方式、帳戶資訊
2. **風險評估** (`WorkflowStep: 2-RiskAssessment`)
   - 包含 `RiskAssessment` 物件：風險分數、風險等級、評估因素
3. **核准或標記** (`WorkflowStep: 3-Approval` 或 `3-FlaggedReview`)
   - 核准：包含 `ApprovalDetails` 物件：核准時間、預計完成時間
   - 標記：包含 `FlagDetails` 物件：原因、需人工審核

## Kafka Topics

| Topic | Producer | 說明 |
|-------|----------|------|
| `bet-settled` | FinanceService | 注單結算完成 |
| `payment-processed` | FinanceService | 支付處理完成 |
| `withdrawal-approved` | FinanceService | 提款已核准 |
| `withdrawal-flagged` | FinanceService | 提款需人工審核 |
| `workflow-completed` | GatewayService | 工作流程完成 |
| `insufficient-balance` | GatewayService | 餘額不足 |

## 錯誤與警告模擬

應用程式透過機率性故障注入產生各種錯誤與警告場景：

| 場景 | 機率 |
|---|---|
| 餘額不足警告 | 20% |
| 遊戲連線延遲 | 15% |
| 餘額不足無法下注 | 10% |
| 結算延遲 | 10% |
| 支付處理器連線錯誤 | 8% |
| 下注限額超出 | 5% |
| 餘額更新失敗 | 5% |
| 支付驗證失敗 | 5% |
| 帳戶凍結 | 5% |

## 可用於查詢的 ID 和欄位

### Workflow 追蹤 ID

- **`TraceId`**: 追蹤單個 Workflow 的所有步驟 (跨服務一致)
- **`CorrelationId`**: 關聯相關的 Workflow (UUID 格式)
- **`UserId`**: 玩家 ID (例如: `USER_123`)

### Workflow 相關欄位

- `WorkflowName`: Workflow 名稱 (`BettingWorkflow`, `DepositWorkflow`, `WithdrawalWorkflow`)
- `WorkflowStep`: Workflow 步驟 (例如: `1-Login`, `5-PlaceBet`)
- `EventType`: 事件類型 (`PlayerLogin`, `BetPlaced`, `BetSettled` 等)
- `service.name`: 微服務名稱（各服務獨立）

### 遊戲相關 ID

- `GameId`: 遊戲 ID (例如: `GAME_45`)
- `BetId`: 注單 ID (UUID 格式)
- `SessionId`: 會話 ID (UUID 格式)
- `TransactionId`: 交易 ID (UUID 格式)
- `TableId`: 桌台 ID (例如: `TABLE_10`)

### 巢狀物件欄位

所有日誌都使用結構化的物件來儲存詳細資訊：

- **LoginContext**: `LoginContext.Device`, `LoginContext.Location`, `LoginContext.IP`
- **BetDetails**: `BetDetails.Amount`, `BetDetails.BetType`, `BetDetails.RemainingBalance`
- **SettlementDetails**: `SettlementDetails.WinAmount`, `SettlementDetails.Profit`, `SettlementDetails.Status`
- **RiskAssessment**: `RiskAssessment.RiskScore`, `RiskAssessment.RiskLevel`, `RiskAssessment.Passed`
- **PaymentDetails**: `PaymentDetails.Amount`, `PaymentDetails.Status`, `PaymentDetails.Fee`

## Seq 查詢範例

**追蹤完整 Workflow：**
```sql
TraceId = '550e8400-e29b-41d4-a716-446655440000'
```

**查詢特定使用者的下注流程：**
```sql
UserId = 'USER_123' and WorkflowName = 'BettingWorkflow'
```

**查詢下注金額大於 100 的記錄：**
```sql
BetDetails.Amount > 100
```

**查詢高風險的提款：**
```sql
RiskAssessment.RiskLevel = 'High'
```

**統計各 Workflow 的數量：**
```sql
select count(*) from stream
where WorkflowName is not null
group by WorkflowName
```

更多查詢範例和進階技巧，請參閱 [SEQ_QUERY_GUIDE.md](SEQ_QUERY_GUIDE.md)

## 技術棧

- **.NET 10.0** / C#
- **gRPC** (Grpc.AspNetCore / Grpc.Net.Client) — 同步服務間通訊
- **Kafka** (Confluent.Kafka, KRaft mode) — 非同步事件驅動
- **Serilog** — 結構化日誌 + OpenTelemetry Sink
- **OpenTelemetry** — 分散式追蹤 (OTLP/gRPC) + gRPC/AspNetCore Instrumentation
- **OpenTelemetry Collector** — 遙測資料路由與轉發
- **Seq** — 結構化日誌平台
- **Grafana** + **Tempo** + **Loki** + **Prometheus** — 可觀測性全家桶
- **Elasticsearch** + **Kibana** — 日誌搜尋與分析

## 停止服務

```bash
# 停止可觀測性基礎設施
docker compose down

# 清除所有資料（含 volumes）
docker compose down -v
```

停止微服務：按 `Ctrl+C` 或使用啟動腳本的停止功能。

## 注意事項

- 此為 Demo 環境，未設置身份驗證
- 生產環境請務必啟用各服務的身份驗證功能
- 所有 Workflow 每 2 秒發送一次
- 事件和數據都是隨機生成的模擬數據
- 啟動順序重要：基礎設施 → FinanceService → PlayerGameService → GatewayService → NotificationService
