# GUIDE.md — 遊戲平台可觀測性展示專案 (Game Platform Observability Demo)

## 專案概述

本專案是一個可觀測性（Observability）綜合演示專案，涵蓋日誌、指標與追蹤。單一 .NET 10.0 主控台應用程式（`dotnet-demo/`）模擬遊戲平台的 7 個微服務，產生真實的遙測數據，並整合 Seq、ELK Stack 及 Grafana Stack 等工具進行收集與視覺化。

### 模擬微服務

1. **ApiGateway** — 入口閘道
2. **PlayerService** — 玩家服務
3. **GameService** — 遊戲服務
4. **WalletService** — 錢包服務
5. **PaymentService** — 支付服務
6. **RiskService** — 風險控管服務
7. **NotificationService** — 通知服務

---

## 專案結構

```
SeqDemo/
├── dotnet-demo/
│   ├── Program.cs                       # 核心程式碼：7 個 ActivitySource、Serilog 設定、三個工作流程實作
│   ├── DotnetSeqDemo.csproj             # 專案定義與相依套件（Serilog, OpenTelemetry）
│   └── ERROR_AND_WARNING_SCENARIOS.md   # 錯誤/警告情境完整目錄
├── config/                              # 各元件設定（OTel Collector、Grafana、Loki、Tempo、Prometheus、Elasticsearch）
├── docker-compose.yml                   # 定義 9 個可觀測性服務容器
├── start-all.bat                        # Windows 快速啟動腳本
└── start-all.sh                         # Linux/Mac 快速啟動腳本
```

---

## 技術堆疊

### 應用程式端
- **語言：** C# / .NET 10.0
- **日誌：** Serilog 4.3 搭配 OpenTelemetry sink，透過 OTLP 匯出
- **追蹤：** OpenTelemetry 1.11（每個模擬微服務各一個 `ActivitySource`）
- **傳輸協定：** OTLP (gRPC) 至 Collector

### 基礎設施（Docker Compose）
- **OpenTelemetry Collector** contrib 0.118.0 — 接收並轉發遙測數據
- **Seq** — 結構化日誌查詢與分析
- **Grafana** — 統一儀表板（整合 Loki、Tempo、Prometheus）
- **Loki** — 日誌聚合後端
- **Tempo** — 分散式追蹤後端
- **Prometheus** — 指標後端
- **Elasticsearch** — 搜尋與分析引擎
- **Kibana** — Elasticsearch 視覺化介面

---

## 資料流架構

```
dotnet-demo (Program.cs)
    │
    ▼ OTLP/gRPC :4317
    │
OTel Collector
    │
    ├──▶ Seq            (:5341)
    ├──▶ Loki           (:3100)
    ├──▶ Tempo          (:3200)
    ├──▶ Elasticsearch  (:9200)
    └──▶ Prometheus     (:9090)
```

### 服務埠號

| 服務              | 埠號                    |
|-------------------|------------------------|
| OTel Collector    | 4317 (gRPC)、4318 (HTTP) |
| Seq               | 5341                   |
| Grafana           | 3000                   |
| Prometheus        | 9090                   |
| Loki              | 3100                   |
| Tempo             | 3200                   |
| Elasticsearch     | 9200                   |
| Kibana            | 5601                   |

---

## 建置與執行

### 快速啟動

```bash
# Windows
start-all.bat

# Linux / Mac
./start-all.sh
```

### 分步操作

```bash
# 1. 啟動基礎設施（等待約 30 秒讓所有服務初始化完成）
docker compose up -d

# 2. 還原套件並執行應用程式
cd dotnet-demo
dotnet restore
dotnet build
dotnet run

# 3. 關閉服務堆疊
docker compose down          # 保留 volumes
docker compose down -v       # 移除 volumes

# 4. 檢視特定服務日誌
docker compose logs <service>
```

---

## 模擬業務邏輯（Workflows）

應用程式持續產生以下三種業務流程，每個工作流程以 2 秒間隔循環執行，並包含機率性的錯誤/警告注入。可透過 `WorkflowName` 進行過濾。

| 工作流程            | 步驟數 | 說明                                               |
|--------------------|--------|---------------------------------------------------|
| **BettingWorkflow**    | 8      | 登入、驗證、餘額檢查、遊戲開始、下注、結果、結算、餘額更新 |
| **DepositWorkflow**    | 4      | 發起、驗證、處理、入帳                               |
| **WithdrawalWorkflow** | 3      | 請求、風險評估、核准/人工審核                         |

錯誤情境的完整目錄、觸發機率與結構化資料 schema 詳見 `dotnet-demo/ERROR_AND_WARNING_SCENARIOS.md`。

---

## 遙測模式

- Serilog 搭配 OpenTelemetry sink，透過 OTLP 匯出日誌
- 每個模擬微服務各有一個 OpenTelemetry `ActivitySource`，用於分散式追蹤
- 透過 `LogContext` 推送結構化日誌屬性（TraceId、CorrelationId、UserId、SessionId）
- 豐富的巢狀物件（LoginContext、BetDetails、RiskAssessment 等）作為結構化日誌資料

### 日誌分析常用欄位

| 欄位             | 說明                                            |
|-----------------|------------------------------------------------|
| `TraceId`       | 追蹤單次完整 Workflow 的唯一標識                    |
| `CorrelationId` | 關聯相關聯的多個 Workflow                          |
| `UserId`        | 模擬玩家 ID（例如 `USER_123`）                     |
| `SessionId`     | 會話 ID                                          |
| `WorkflowName`  | 工作流程名稱                                      |
| `WorkflowStep`  | 具體流程步驟（例如 `5-PlaceBet`）                   |

**查詢範例：** 若要尋找特定使用者的下注歷史，請過濾 `UserId` 並篩選 `WorkflowName = 'BettingWorkflow'`。

---

## 程式碼風格與規範

- C# 啟用可為 null 的參考型別與 implicit usings（見 `DotnetSeqDemo.csproj`）
- 4 空白縮排
- 公開型別與成員使用 `PascalCase`，區域變數與參數使用 `camelCase`
- 日誌與 enrichment 模式請維持與 `dotnet-demo/Program.cs` 一致
- 變更應聚焦且最小化

---

## 測試指引

- 目前沒有自動化測試
- 若新增測試，建議建立 `tests/SeqDemo.Tests`，並以 `dotnet test` 執行
- 測試類別以 `*Tests` 結尾，測試資料與情境保持就近放置

---

## Commit 與 Pull Request 規範

### Commit 格式
```
<emoji> <type>(scope): summary
```
範例：`✨ feat(readme): update docs`

建議使用的 type：`feat`、`refactor`、`chore`，以簡短命令式摘要描述變更。

### Pull Request 要求
- 說明目的與行為變更
- 描述如何執行與測試狀態
- 若修改可觀測性設定，需說明影響的服務與驗證方式

---

## 安全與設定注意事項

- 預設端點以 `docker-compose.yml` 為準，避免硬編碼機密
- 新增環境變數時，請同步更新 `README.md`

---

## 故障排除

- **基礎設施未啟動：** 檢查 `docker logs <container_name>`
- **應用程式連線失敗：** 確保程式能成功連接到 `localhost:4317`（OTel Collector）
- **服務初始化：** 啟動基礎設施後，請等待約 30 秒讓所有服務初始化完成
