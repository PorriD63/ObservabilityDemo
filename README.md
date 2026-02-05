# Seq 多語言日誌 Demo

此專案示範如何使用 Seq 集中收集來自不同語言應用程式的結構化日誌，模擬遊戲平台的各種事件。

## 專案結構

```
.
├── docker-compose.yml      # Seq 服務配置
├── golang-demo/           # Go 應用程式範例
│   ├── main.go
│   ├── go.mod
│   └── README.md
├── nodejs-demo/           # Node.js 應用程式範例
│   ├── index.js
│   ├── package.json
│   └── README.md
└── dotnet-demo/           # .NET 應用程式範例
    ├── Program.cs
    ├── DotnetSeqDemo.csproj
    └── README.md
```

## 快速開始

### 1. 啟動 Seq 服務

```bash
docker-compose up -d
```

Seq 將會在以下位置運行：
- Web UI: http://localhost:5341
- Ingestion endpoint: http://localhost:5341

等待幾秒鐘讓 Seq 完全啟動後，開啟瀏覽器訪問 http://localhost:5341

### 2. 運行範例應用程式

你可以同時運行所有三個應用程式，它們會將日誌發送到同一個 Seq 實例。

#### Go 應用程式

```bash
cd golang-demo
go mod download
go run main.go
```

#### Node.js 應用程式

```bash
cd nodejs-demo
npm install
npm start
```

#### .NET 應用程式

```bash
cd dotnet-demo
dotnet restore
dotnet run
```

### 3. 查看日誌

開啟瀏覽器訪問 http://localhost:5341 即可查看所有應用程式的日誌。

## 模擬的遊戲平台 Workflow

所有三個應用程式都會模擬完整的業務流程（Workflow），每個 workflow 包含多個步驟，並使用 `TraceId`、`CorrelationId`、`UserId` 串聯所有相關日誌。

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

## 可用於查詢的 ID 和欄位

### 🔑 Workflow 追蹤 ID（最重要！）

- **`TraceId`**: 追蹤單個 workflow 的所有步驟 (UUID 格式)
  - 用途：查看完整的業務流程（從開始到結束）
  - 範例：`TraceId = '550e8400-e29b-41d4-a716-446655440000'`

- **`CorrelationId`**: 關聯相關的 workflow (UUID 格式)
  - 用途：關聯跨多個 workflow 的事件
  - 範例：`CorrelationId = '550e8400-e29b-41d4-a716-446655440000'`

- **`UserId`**: 玩家 ID (例如: `USER_123`)
  - 用途：查看特定使用者的所有活動
  - 範例：`UserId = 'USER_123'`

### 📝 Workflow 相關欄位

- `WorkflowName`: Workflow 名稱 (`BettingWorkflow`, `DepositWorkflow`, `WithdrawalWorkflow`)
- `WorkflowStep`: Workflow 步驟 (例如: `1-Login`, `5-PlaceBet`)
- `EventType`: 事件類型 (`PlayerLogin`, `BetPlaced`, `BetSettled` 等)

### 🎮 遊戲相關 ID

- `GameId`: 遊戲 ID (例如: `GAME_45`)
- `BetId`: 注單 ID (UUID 格式)
- `SessionId`: 會話 ID (UUID 格式)
- `TransactionId`: 交易 ID (UUID 格式)
- `TableId`: 桌台 ID (例如: `TABLE_10`)

### 📊 巢狀物件欄位（使用物件形式的參數資料）

所有日誌都使用結構化的物件來儲存詳細資訊：

- **LoginContext**: 登入相關資訊
  - `LoginContext.Device`: 設備類型
  - `LoginContext.Location`: 地區
  - `LoginContext.IP`: IP 位址

- **BetDetails**: 下注詳細資訊
  - `BetDetails.Amount`: 下注金額
  - `BetDetails.BetType`: 下注類型
  - `BetDetails.RemainingBalance`: 剩餘餘額

- **SettlementDetails**: 結算詳細資訊
  - `SettlementDetails.WinAmount`: 贏得金額
  - `SettlementDetails.Profit`: 利潤
  - `SettlementDetails.Status`: 狀態

- **RiskAssessment**: 風險評估資訊
  - `RiskAssessment.RiskScore`: 風險分數
  - `RiskAssessment.RiskLevel`: 風險等級
  - `RiskAssessment.Passed`: 是否通過

- **PaymentDetails**: 支付詳細資訊
  - `PaymentDetails.Amount`: 金額
  - `PaymentDetails.Status`: 狀態
  - `PaymentDetails.Fee`: 手續費

## Seq 查詢範例

### 🎯 追蹤完整 Workflow（最重要的功能！）

**查詢某個 TraceId 的完整流程：**
```sql
TraceId = '550e8400-e29b-41d4-a716-446655440000'
```
這會顯示從開始到結束的所有步驟，按時間排序。

**查詢某個使用者的所有下注流程：**
```sql
UserId = 'USER_123' and WorkflowName = 'BettingWorkflow'
```

**查詢特定 Workflow 的特定步驟：**
```sql
WorkflowName = 'BettingWorkflow' and WorkflowStep = '5-PlaceBet'
```

### 🔍 使用巢狀物件查詢

**查詢來自 iOS 設備的登入：**
```sql
LoginContext.Device = 'iOS'
```

**查詢下注金額大於 100 的記錄：**
```sql
BetDetails.Amount > 100
```

**查詢有獲利的注單：**
```sql
SettlementDetails.Profit > 0
```

**查詢高風險的提款：**
```sql
RiskAssessment.RiskLevel = 'High'
```

**查詢使用信用卡的存款：**
```sql
DepositRequest.PaymentMethod = 'CreditCard'
```

### 📊 實用查詢範例

**追蹤單筆下注的完整流程：**

步驟 1：找到一筆下注
```sql
EventType = 'BetPlaced' and BetDetails.Amount > 100
```

步驟 2：複製 TraceId，查看完整流程
```sql
TraceId = 'your-trace-id-from-step-1'
```

**查詢支付失敗並查看原因：**
```sql
EventType = 'PaymentFailed'
```

**查詢特定使用者的總盈虧：**
```sql
select sum(SettlementDetails.Profit) as TotalProfit
from stream
where UserId = 'USER_123' and EventType = 'BetSettled'
```

**統計各 Workflow 的數量：**
```sql
select count(*) from stream
where WorkflowName is not null
group by WorkflowName
```

**查詢各遊戲類型的下注次數：**
```sql
select count(*) from stream
where EventType = 'BetPlaced'
group by GameDetails.GameType
```

### 📖 完整查詢指南

更多查詢範例和進階技巧，請參閱 [SEQ_QUERY_GUIDE.md](SEQ_QUERY_GUIDE.md)

## Seq 功能特點

- **結構化日誌**: 支援 JSON 格式的結構化日誌
- **強大查詢**: 使用 SQL-like 語法查詢日誌
- **即時監控**: 實時查看應用程式日誌
- **多語言支援**: 支援多種程式語言
- **日誌等級**: 支援 Verbose, Debug, Information, Warning, Error, Fatal
- **過濾和搜尋**: 可以根據任何欄位進行過濾
- **時間範圍**: 可以選擇特定時間範圍的日誌
- **儀表板**: 可以創建自定義儀表板監控關鍵指標

## 技術棧

- **Seq**: Latest (Docker)
- **Go**: 使用標準庫 HTTP client 直接發送到 Seq API
- **Node.js**: 使用 `winston` + `winston-seq`
- **.NET**: 使用 `Serilog` + `Serilog.Sinks.Seq`

## 停止服務

停止 Seq 服務：
```bash
docker-compose down
```

停止所有應用程式：按 `Ctrl+C`

## 清除數據

如果需要清除 Seq 中的所有數據：

```bash
docker-compose down -v
```

## 注意事項

- 此為 Demo 環境，未設置身份驗證
- 生產環境請務必啟用 Seq 的身份驗證功能
- 預設 Seq 數據儲存在 Docker volume 中
- 所有應用程式每 2 秒發送一次日誌
- 事件和數據都是隨機生成的模擬數據

## 疑難排解

### Seq 無法啟動
確保 Docker 正在運行，且端口 5341 未被佔用。

### 應用程式無法連接到 Seq
確保 Seq 已經完全啟動（等待約 10 秒），然後再啟動應用程式。

### Go 模組錯誤
```bash
cd golang-demo
go mod tidy
```

### Node.js 依賴錯誤
```bash
cd nodejs-demo
rm -rf node_modules package-lock.json
npm install
```
