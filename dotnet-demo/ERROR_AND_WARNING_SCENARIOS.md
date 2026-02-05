# Error 和 Warning 場景說明

本文檔列出所有在 .NET Demo 中實現的錯誤和警告場景。

## BettingWorkflow (下注流程)

### ⚠️ Warnings

#### 1. 低餘額警告 (WorkflowStep: 3-BalanceCheck)
- **EventType**: `BalanceChecked`
- **觸發機率**: 20% (當餘額 < 500 時)
- **日誌等級**: Warning
- **訊息**: "低餘額警告: {UserId} 餘額僅剩 {Balance} {Currency}"
- **查詢範例**:
  ```sql
  WorkflowName = 'BettingWorkflow' and @Message like '%低餘額警告%'
  ```

#### 2. 遊戲連線延遲 (WorkflowStep: 4-GameStart)
- **EventType**: `GameStarted`
- **觸發機率**: 15%
- **日誌等級**: Warning
- **結構化資料**: `ConnectionIssue` (包含 Issue, Latency, TableId)
- **訊息**: "遊戲連線延遲: {TableId} 延遲 {Latency}ms"
- **查詢範例**:
  ```sql
  ConnectionIssue.Latency > 1000
  ```

#### 3. 下注超過限額 (WorkflowStep: 5-PlaceBet)
- **EventType**: `BetLimitExceeded`
- **觸發機率**: 5% (當下注金額 > 1000 時)
- **日誌等級**: Warning
- **結構化資料**: `LimitError` (包含 RequestedAmount, MaxLimit, ExcessAmount)
- **訊息**: "下注警告 - 超過限額"
- **查詢範例**:
  ```sql
  EventType = 'BetLimitExceeded'
  ```

#### 4. 結算延遲 (WorkflowStep: 7-Settlement)
- **EventType**: `BetSettled`
- **觸發機率**: 10%
- **日誌等級**: Warning
- **結構化資料**: `DelayInfo` (包含 DelaySeconds, Reason)
- **訊息**: "結算延遲: Transaction {TransactionId} 延遲 {DelaySeconds} 秒"
- **查詢範例**:
  ```sql
  DelayInfo.DelaySeconds > 5
  ```

### ❌ Errors

#### 1. 餘額不足 (WorkflowStep: 5-PlaceBet)
- **EventType**: `BetRejected`
- **觸發機率**: 10%
- **日誌等級**: Error
- **結構化資料**: `InsufficientError` (包含 RequestedAmount, AvailableBalance, Shortage)
- **訊息**: "下注失敗 - 餘額不足"
- **結果**: **Workflow 終止**
- **查詢範例**:
  ```sql
  EventType = 'BetRejected' and InsufficientError.Shortage > 0
  ```

#### 2. 餘額更新失敗 (WorkflowStep: 8-BalanceUpdate)
- **EventType**: `BalanceUpdated`
- **觸發機率**: 5%
- **日誌等級**: Error
- **結構化資料**: `UpdateError` (包含 ErrorCode, ErrorMessage, RetryAttempt)
- **訊息**: "餘額更新失敗: Transaction {TransactionId}"
- **查詢範例**:
  ```sql
  UpdateError.ErrorCode = 'BALANCE_UPDATE_FAILED'
  ```

---

## DepositWorkflow (存款流程)

### ⚠️ Warnings

#### 1. 金額超過限制 (WorkflowStep: 1-InitiateDeposit)
- **EventType**: `DepositInitiated`
- **觸發機率**: 15% (當金額 > 3000 時)
- **日誌等級**: Warning
- **結構化資料**: `LimitWarning` (包含 DailyLimit, SingleTransactionLimit, RequiresAdditionalVerification)
- **訊息**: "存款金額警告: 接近限額，需要額外驗證"
- **查詢範例**:
  ```sql
  LimitWarning.RequiresAdditionalVerification = true
  ```

#### 2. 支付處理器連線問題 (WorkflowStep: 3-ProcessPayment)
- **EventType**: `PaymentProcessorConnectionError`
- **觸發機率**: 8%
- **日誌等級**: Warning
- **結構化資料**: `ConnectionError` (包含 ProcessorId, ErrorType, RetryCount)
- **訊息**: "支付處理器連線問題: 連線超時"
- **查詢範例**:
  ```sql
  EventType = 'PaymentProcessorConnectionError' and ConnectionError.RetryCount > 2
  ```

### ❌ Errors

#### 1. 驗證失敗 (WorkflowStep: 2-ValidatePayment)
- **EventType**: `PaymentValidationFailed`
- **觸發機率**: 5%
- **日誌等級**: Error
- **結構化資料**: `ValidationDetails` (包含 IsValid, FailureReason)
- **失敗原因**: `KYC_NOT_VERIFIED`, `PAYMENT_METHOD_SUSPENDED`, `ACCOUNT_RESTRICTED`
- **訊息**: "驗證失敗: 支付方式驗證失敗"
- **結果**: **Workflow 終止**
- **查詢範例**:
  ```sql
  EventType = 'PaymentValidationFailed' and ValidationDetails.FailureReason = 'KYC_NOT_VERIFIED'
  ```

#### 2. 處理支付失敗 (WorkflowStep: 3-ProcessPayment)
- **EventType**: `PaymentFailed`
- **觸發機率**: 10%
- **日誌等級**: Error
- **結構化資料**: `PaymentDetails` (包含 Status, ErrorCode)
- **錯誤代碼**: `INSUFFICIENT_FUNDS`, `CARD_DECLINED`, `BANK_REJECTION`, `FRAUD_DETECTED`
- **訊息**: "處理支付失敗: Transaction {TransactionId}"
- **查詢範例**:
  ```sql
  EventType = 'PaymentFailed' and PaymentDetails.ErrorCode = 'FRAUD_DETECTED'
  ```

---

## WithdrawalWorkflow (提款流程)

### ⚠️ Warnings

#### 1. KYC 未完成 (WorkflowStep: 1-RequestWithdrawal)
- **EventType**: `WithdrawalRequested`
- **觸發機率**: 10%
- **日誌等級**: Warning
- **結構化資料**: `KYCWarning` (包含 KYCStatus, MissingDocuments, RequiredForAmount)
- **訊息**: "KYC 警告: KYC 未完成，缺少文件"
- **查詢範例**:
  ```sql
  KYCWarning.KYCStatus = 'Incomplete'
  ```

#### 2. 超過每日提款限制 (WorkflowStep: 1-RequestWithdrawal)
- **EventType**: `WithdrawalRequested`
- **觸發機率**: 8% (當金額 > 2000 時)
- **日誌等級**: Warning
- **結構化資料**: `DailyLimitWarning` (包含 DailyLimit, TodayWithdrawn, RemainingLimit)
- **訊息**: "每日提款限額警告"
- **查詢範例**:
  ```sql
  DailyLimitWarning.RemainingLimit < 1000
  ```

#### 3. 高風險提款 (WorkflowStep: 2-RiskAssessment)
- **EventType**: `RiskAssessed`
- **觸發機率**: ~30% (當 RiskScore >= 70 時)
- **日誌等級**: Warning
- **結構化資料**: `RiskAssessment` (包含 RiskScore, RiskLevel, Passed)
- **訊息**: "高風險提款: 需要人工審核"
- **查詢範例**:
  ```sql
  RiskAssessment.RiskLevel = 'High'
  ```

#### 4. 中風險提款 (WorkflowStep: 2-RiskAssessment)
- **EventType**: `RiskAssessed`
- **觸發機率**: ~40% (當 30 <= RiskScore < 70 時)
- **日誌等級**: Warning
- **訊息**: "中風險提款"
- **查詢範例**:
  ```sql
  RiskAssessment.RiskLevel = 'Medium'
  ```

#### 5. 異常交易模式 (WorkflowStep: 2-RiskAssessment)
- **EventType**: `RiskAssessed`
- **觸發機率**: 10%
- **日誌等級**: Warning
- **結構化資料**: `PatternWarning` (包含 Pattern, Confidence, RequiresReview)
- **異常模式**: `FrequentWithdrawals`, `LargeAmountAfterDeposit`, `UnusualTiming`, `NewDevice`
- **訊息**: "異常交易模式: 偵測到 {Pattern}"
- **查詢範例**:
  ```sql
  PatternWarning.Confidence > 80
  ```

#### 6. 提款標記審核 (WorkflowStep: 3-FlaggedReview)
- **EventType**: `WithdrawalFlagged`
- **觸發機率**: ~30% (當風險評估未通過時)
- **日誌等級**: Warning
- **結構化資料**: `FlagDetails` (包含 Reason, RequiresManualReview, ReviewerAssigned)
- **標記原因**: `HighRiskScore`, `UnusualPattern`, `LargeAmount`, `NewAccount`
- **訊息**: "提款標記審核: 需要人工審核"
- **查詢範例**:
  ```sql
  EventType = 'WithdrawalFlagged' and FlagDetails.Reason = 'HighRiskScore'
  ```

### ❌ Errors

#### 1. 餘額不足 (WorkflowStep: 1-RequestWithdrawal)
- **EventType**: `WithdrawalRejectedInsufficientBalance`
- **觸發機率**: 12%
- **日誌等級**: Error
- **結構化資料**: `InsufficientBalanceError` (包含 RequestedAmount, AvailableBalance, Shortage)
- **訊息**: "提款失敗 - 餘額不足"
- **結果**: **Workflow 終止**
- **查詢範例**:
  ```sql
  EventType = 'WithdrawalRejectedInsufficientBalance'
  ```

#### 2. 帳戶凍結 (WorkflowStep: 1-RequestWithdrawal)
- **EventType**: `WithdrawalRejectedAccountFrozen`
- **觸發機率**: 5%
- **日誌等級**: Error
- **結構化資料**: `AccountFrozenError` (包含 Reason, FrozenSince, ContactSupport)
- **凍結原因**: `SUSPECTED_FRAUD`, `PENDING_INVESTIGATION`, `COMPLIANCE_HOLD`, `DUPLICATE_ACCOUNT`
- **訊息**: "提款失敗 - 帳戶凍結"
- **結果**: **Workflow 終止**
- **查詢範例**:
  ```sql
  EventType = 'WithdrawalRejectedAccountFrozen' and AccountFrozenError.Reason = 'SUSPECTED_FRAUD'
  ```

---

## 統計查詢範例

### 查看所有錯誤

```sql
@Level = 'Error'
```

### 查看所有警告

```sql
@Level = 'Warning'
```

### 統計各類型錯誤數量

```sql
select count(*) as ErrorCount, EventType
from stream
where @Level = 'Error'
group by EventType
order by ErrorCount desc
```

### 查看導致 Workflow 終止的錯誤

```sql
@Message like '%而終止%'
```

### 查看特定使用者的所有錯誤和警告

```sql
UserId = 'USER_123' and (@Level = 'Error' or @Level = 'Warning')
```

### 查看高風險相關的警告

```sql
(RiskAssessment.RiskLevel = 'High') or (EventType = 'WithdrawalFlagged')
```

### 查看所有支付相關錯誤

```sql
@Level = 'Error' and (EventType like '%Payment%' or EventType like '%Deposit%')
```

### 追蹤失敗的 Workflow

```sql
TraceId = 'your-trace-id' and (@Level = 'Error' or @Level = 'Warning')
```

---

## 日誌等級分佈

- **Information**: 正常業務流程（約 70-80%）
- **Warning**: 需要注意但不影響流程（約 15-25%）
- **Error**: 嚴重錯誤，可能終止 Workflow（約 5-10%）

## 注意事項

1. 所有錯誤和警告都保留完整的 `TraceId`，可以追蹤整個 workflow
2. 導致 workflow 終止的錯誤會在日誌中標記 "✗ {WorkflowName} 因{原因}而終止"
3. 所有結構化資料都可以在 Seq 中直接查詢
4. 機率是隨機的，實際比例可能會有波動
