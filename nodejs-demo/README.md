# Node.js Seq Demo

此專案示範如何使用 Node.js、Winston 和 Seq 記錄遊戲平台的結構化日誌。

## 技術棧

- **Node.js** (v18+ 推薦)
- **Winston** - 強大的日誌框架
- **@datalust/winston-seq** - Seq 官方傳輸層
- **uuid** - UUID 生成

## 專案結構

```
nodejs-demo/
├── index.js       # 主程式，包含三個 workflow
├── package.json   # 專案配置
└── README.md      # 說明文件
```

## 快速開始

### 1. 確保 Seq 服務已啟動

在專案根目錄執行：

```bash
docker-compose up -d
```

等待 Seq 完全啟動後（約 10 秒），訪問 http://localhost:5341

### 2. 安裝依賴

```bash
cd nodejs-demo
npm install
```

### 3. 運行應用程式

```bash
npm start
```

或使用開發模式（自動重載）：

```bash
npm run dev
```

### 4. 查看日誌

開啟瀏覽器訪問 http://localhost:5341，即可看到三個 workflow 的日誌流。

## 🎯 日誌等級與錯誤處理

本專案包含豐富的錯誤和警告場景，模擬真實的業務情況：

- **info** (70-80%): 正常業務流程
- **warn** (15-25%): 需要注意的情況，但不會終止 workflow
- **error** (5-10%): 嚴重錯誤，可能導致 workflow 終止

### 常見錯誤類型

- **BettingWorkflow**: 餘額不足、下注超限、連線延遲、結算延遲
- **DepositWorkflow**: 驗證失敗、支付失敗、金額超限、處理器連線問題
- **WithdrawalWorkflow**: 餘額不足、帳戶凍結、高風險交易、KYC 未完成

## 實現的 Workflow

### 1. 下注流程 (BettingWorkflow) - 8 步驟

完整的遊戲下注流程，每個 `TraceId` 串聯 8 個步驟：

1. **1-Login**: 玩家登入（包含登入上下文：IP、設備、瀏覽器、地區）
2. **2-Authentication**: 玩家驗證（包含驗證詳情：方式、Token、角色）
3. **3-BalanceCheck**: 查詢餘額（包含餘額資訊）
   - ⚠️ Warning: 低餘額警告 (20%)
4. **4-GameStart**: 遊戲開始（包含遊戲詳情：類型、桌台、荷官）
   - ⚠️ Warning: 遊戲連線延遲 (15%)
5. **5-PlaceBet**: 下注（包含下注詳情：金額、類型、剩餘餘額）
   - ⚠️ Warning: 下注超過限額 (5%)
   - ❌ Error: 餘額不足 (10%) → **Workflow 終止**
6. **6-GameResult**: 遊戲結果（包含結果詳情：結果、牌面）
7. **7-Settlement**: 注單結算（包含結算詳情：贏得金額、利潤）
   - ⚠️ Warning: 結算延遲 (10%)
8. **8-BalanceUpdate**: 餘額更新（包含餘額變更詳情）
   - ❌ Error: 餘額更新失敗 (5%)

**範例查詢**：
```sql
-- 查看完整下注流程
TraceId = 'your-trace-id' and WorkflowName = 'BettingWorkflow'

-- 查看所有下注步驟
WorkflowName = 'BettingWorkflow' and WorkflowStep = '5-PlaceBet'

-- 查看獲利的注單
SettlementDetails.Profit > 0

-- 查看餘額不足的錯誤
EventType = 'BetRejected'
```

### 2. 存款流程 (DepositWorkflow) - 4 步驟

完整的存款流程，每個 `TraceId` 串聯 4 個步驟：

1. **1-InitiateDeposit**: 發起存款（包含存款請求：金額、支付方式）
   - ⚠️ Warning: 金額超過限制 (15%)
2. **2-ValidatePayment**: 驗證支付方式（包含驗證詳情）
   - ❌ Error: 驗證失敗 (5%) → **Workflow 終止**
3. **3-ProcessPayment**: 處理支付（包含支付詳情：交易ID、狀態、手續費）
   - ⚠️ Warning: 支付處理器連線問題 (8%)
   - ❌ Error: 處理支付失敗 (10%)
4. **4-CreditBalance**: 餘額入帳（僅在成功時，包含入帳詳情）

**範例查詢**：
```sql
-- 查看完整存款流程
TraceId = 'your-trace-id' and WorkflowName = 'DepositWorkflow'

-- 查看失敗的支付
EventType = 'PaymentFailed'

-- 查看使用信用卡的存款
DepositRequest.PaymentMethod = 'CreditCard'

-- 查看驗證失敗的原因
EventType = 'PaymentValidationFailed'
```

### 3. 提款流程 (WithdrawalWorkflow) - 3 步驟

完整的提款流程，每個 `TraceId` 串聯 3 個步驟：

1. **1-RequestWithdrawal**: 提款請求（包含提款請求：金額、方式、帳戶）
   - ⚠️ Warning: KYC 未完成 (10%)
   - ⚠️ Warning: 超過每日提款限制 (8%)
   - ❌ Error: 餘額不足 (12%) → **Workflow 終止**
   - ❌ Error: 帳戶凍結 (5%) → **Workflow 終止**
2. **2-RiskAssessment**: 風險評估（包含風險評估：分數、等級）
   - ⚠️ Warning: 高風險提款 (~30%)
   - ⚠️ Warning: 中風險提款 (~40%)
   - ⚠️ Warning: 異常交易模式 (10%)
3. **3-Approval** 或 **3-FlaggedReview**: 核准或標記審核
   - 核准：包含核准詳情（預計完成時間）
   - ⚠️ Warning: 標記：需人工審核 (~30%)

**範例查詢**：
```sql
-- 查看完整提款流程
TraceId = 'your-trace-id' and WorkflowName = 'WithdrawalWorkflow'

-- 查看高風險提款
RiskAssessment.RiskLevel = 'High'

-- 查看被標記的提款
EventType = 'WithdrawalFlagged'

-- 查看帳戶凍結的錯誤
EventType = 'WithdrawalRejectedAccountFrozen'
```

## 關鍵追蹤欄位

每個 workflow 實例都包含以下關鍵欄位用於追蹤：

- **`TraceId`**: 追蹤單個 workflow 的所有步驟（UUID）
- **`CorrelationId`**: 關聯相關的 workflow（UUID）
- **`UserId`**: 玩家 ID（格式：`USER_###`）
- **`SessionId`**: 會話 ID（UUID）
- **`WorkflowName`**: Workflow 名稱（`BettingWorkflow`, `DepositWorkflow`, `WithdrawalWorkflow`）
- **`WorkflowStep`**: 當前步驟（例如：`1-Login`, `5-PlaceBet`）
- **`EventType`**: 事件類型（例如：`PlayerLogin`, `BetPlaced`, `BetSettled`）

## Seq 查詢範例

### 追蹤完整 Workflow

最重要的功能是使用 `TraceId` 追蹤完整的業務流程：

```sql
-- 查看某個 TraceId 的完整流程（所有步驟按時間排序）
TraceId = '550e8400-e29b-41d4-a716-446655440000'

-- 查看某個使用者的所有下注流程
UserId = 'USER_123' and WorkflowName = 'BettingWorkflow'

-- 查看特定步驟
WorkflowName = 'BettingWorkflow' and WorkflowStep = '5-PlaceBet'
```

### 使用結構化物件查詢

```sql
-- 查看來自 iOS 設備的登入
LoginContext.Device = 'iOS'

-- 查看下注金額大於 100 的記錄
BetDetails.Amount > 100

-- 查看有獲利的注單
SettlementDetails.Profit > 0

-- 查看高風險提款
RiskAssessment.RiskLevel = 'High'

-- 查看使用信用卡的存款
DepositRequest.PaymentMethod = 'CreditCard'
```

### 聚合查詢

```sql
-- 統計各 Workflow 的數量
select count(*) from stream
where WorkflowName is not null
group by WorkflowName

-- 計算總盈虧
select sum(SettlementDetails.Profit) as TotalProfit
from stream
where EventType = 'BetSettled'

-- 統計各遊戲類型的下注次數
select count(*) from stream
where EventType = 'BetPlaced'
group by GameDetails.GameType
```

### 錯誤和警告查詢

```sql
-- 查看所有錯誤
@Level = 'error'

-- 查看所有警告
@Level = 'warn'

-- 查看導致 workflow 終止的錯誤
@Message like '%而終止%'

-- 統計各類型錯誤數量
select count(*) as ErrorCount, EventType
from stream
where @Level = 'error'
group by EventType
order by ErrorCount desc

-- 查看高風險提款
RiskAssessment.RiskLevel = 'High'

-- 查看支付失敗的原因
EventType = 'PaymentFailed'

-- 查看帳戶凍結的情況
EventType = 'WithdrawalRejectedAccountFrozen'

-- 追蹤包含錯誤的完整 workflow
TraceId = 'your-trace-id' and (@Level = 'error' or @Level = 'warn')
```

## 程式碼說明

### Winston + Seq 配置

```javascript
const winston = require('winston');
const { SeqTransport } = require('@datalust/winston-seq');

const logger = winston.createLogger({
  level: 'debug',
  format: winston.format.combine(
    winston.format.errors({ stack: true }),
    winston.format.json()
  ),
  defaultMeta: {
    Application: 'Node.js',
    Environment: 'Demo'
  },
  transports: [
    new winston.transports.Console(),
    new SeqTransport({
      serverUrl: 'http://localhost:5341'
    })
  ]
});
```

### 添加結構化屬性

```javascript
logger.info('下注', {
  TraceId: traceId,
  WorkflowName: 'BettingWorkflow',
  WorkflowStep: '5-PlaceBet',
  EventType: 'BetPlaced',
  BetDetails: {
    Amount: betAmount,
    Currency: 'USD',
    BetType: 'Player'
  }
});
```

### 並行執行三個 Workflow

```javascript
Promise.all([
  runBettingWorkflow(),
  runDepositWorkflow(),
  runWithdrawalWorkflow()
]);
```

## 停止應用程式

按 `Ctrl+C` 停止應用程式，程式會優雅地關閉。

## 調整日誌頻率

預設每個 workflow 每 2 秒執行一次。如需調整頻率，修改各 workflow 函數中的 `await delay(2000)` 參數（單位：毫秒）。

## 開發建議

### 1. 添加新的 Workflow

複製現有的 workflow 函數，並：
- 定義新的 workflow 名稱
- 設計 workflow 步驟
- 為每個步驟創建結構化物件
- 添加追蹤資訊到每個日誌

### 2. 添加更多結構化資料

```javascript
const customData = {
  Field1: 'value1',
  Field2: 123,
  NestedObject: { SubField: 'value' }
};

logger.info('Custom event', {
  ...baseContext,
  CustomData: customData
});
```

### 3. 使用不同的日誌等級

```javascript
logger.debug('Debug message', { ...context });
logger.info('Info message', { ...context });
logger.warn('Warning message', { ...context });
logger.error('Error message', { ...context });
```

## 疑難排解

### 連接不到 Seq

確保：
1. Node.js 版本 >= 14
2. Seq 容器已啟動：`docker ps | grep seq`
3. Seq URL 正確：`http://localhost:5341`

### npm 套件錯誤

```bash
rm -rf node_modules package-lock.json
npm install
```

### 日誌沒有出現在 Seq

檢查：
1. 應用程式是否有錯誤訊息
2. Seq 服務是否正常運行
3. 網路連接是否正常
4. @datalust/winston-seq 套件是否正確安裝

## 進階功能

### 使用環境變數配置 Seq URL

```javascript
const seqUrl = process.env.SEQ_URL || 'http://localhost:5341';

new SeqTransport({
  serverUrl: seqUrl
});
```

### 添加全域屬性

```javascript
const logger = winston.createLogger({
  defaultMeta: {
    Application: 'Node.js',
    Environment: process.env.NODE_ENV || 'development',
    Version: '1.0.0'
  }
});
```

### 使用 API Key（生產環境）

```javascript
new SeqTransport({
  serverUrl: 'http://localhost:5341',
  apiKey: 'your-api-key-here'
});
```

## 完整錯誤場景列表

### BettingWorkflow
- ⚠️ 低餘額警告 (20%)
- ⚠️ 遊戲連線延遲 (15%)
- ⚠️ 下注超過限額 (5%)
- ⚠️ 結算延遲 (10%)
- ❌ 餘額不足 (10%) - 終止
- ❌ 餘額更新失敗 (5%)

### DepositWorkflow
- ⚠️ 金額超過限制 (15%)
- ⚠️ 支付處理器連線問題 (8%)
- ❌ 驗證失敗 (5%) - 終止
- ❌ 處理支付失敗 (10%)

### WithdrawalWorkflow
- ⚠️ KYC 未完成 (10%)
- ⚠️ 超過每日提款限制 (8%)
- ⚠️ 高風險提款 (~30%)
- ⚠️ 中風險提款 (~40%)
- ⚠️ 異常交易模式 (10%)
- ⚠️ 提款標記審核 (~30%)
- ❌ 餘額不足 (12%) - 終止
- ❌ 帳戶凍結 (5%) - 終止

## 更多資訊

- [Winston 文檔](https://github.com/winstonjs/winston)
- [@datalust/winston-seq 文檔](https://github.com/datalust/winston-seq)
- [Seq 文檔](https://docs.datalust.co/docs)
- [主專案 README](../README.md)
- [Seq 查詢指南](../SEQ_QUERY_GUIDE.md)
