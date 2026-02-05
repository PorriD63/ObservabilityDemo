package main

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"math/rand"
	"net/http"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"github.com/google/uuid"
)

const seqURL = "http://localhost:5341/api/events/raw?clef"

// LogLevel represents the severity of a log event
type LogLevel string

const (
	LogLevelDebug   LogLevel = "Debug"
	LogLevelInfo    LogLevel = "Information"
	LogLevelWarning LogLevel = "Warning"
	LogLevelError   LogLevel = "Error"
	LogLevelFatal   LogLevel = "Fatal"
)

// SeqEvent represents a structured log event for Seq
type SeqEvent struct {
	Timestamp       time.Time   `json:"@t"`
	MessageTemplate string      `json:"@mt"`
	Level           LogLevel    `json:"@l,omitempty"`
	Properties      interface{} `json:"-"` // 不直接序列化
}

// SeqLogger handles logging to Seq
type SeqLogger struct {
	client *http.Client
}

// NewSeqLogger creates a new Seq logger
func NewSeqLogger() *SeqLogger {
	return &SeqLogger{
		client: &http.Client{
			Timeout: 5 * time.Second,
		},
	}
}

// Log sends a log event to Seq
func (l *SeqLogger) Log(level LogLevel, messageTemplate string, properties map[string]interface{}) {
	// Create a flat map with all properties
	flatEvent := make(map[string]interface{})

	// Add Seq special properties
	flatEvent["@t"] = time.Now().UTC().Format(time.RFC3339Nano)
	flatEvent["@mt"] = messageTemplate
	if level != "" {
		flatEvent["@l"] = level
	}

	// Add default properties
	flatEvent["Application"] = "Go"
	flatEvent["Environment"] = "Demo"

	// Add user properties
	if properties != nil {
		for k, v := range properties {
			flatEvent[k] = v
		}
	}

	jsonData, err := json.Marshal(flatEvent)
	if err != nil {
		fmt.Printf("Error marshaling log event: %v\n", err)
		return
	}

	// Also print to console
	fmt.Printf("[%s] %s %v\n", level, messageTemplate, properties)

	// Send to Seq
	resp, err := l.client.Post(seqURL, "application/vnd.serilog.clef", bytes.NewBuffer(jsonData))
	if err != nil {
		fmt.Printf("Error sending log to Seq: %v\n", err)
		return
	}
	defer resp.Body.Close()

	if resp.StatusCode != http.StatusCreated && resp.StatusCode != http.StatusOK {
		fmt.Printf("Seq returned status: %d\n", resp.StatusCode)
	}
}

// Helper methods for different log levels
func (l *SeqLogger) Debug(messageTemplate string, properties map[string]interface{}) {
	l.Log(LogLevelDebug, messageTemplate, properties)
}

func (l *SeqLogger) Info(messageTemplate string, properties map[string]interface{}) {
	l.Log(LogLevelInfo, messageTemplate, properties)
}

func (l *SeqLogger) Warning(messageTemplate string, properties map[string]interface{}) {
	l.Log(LogLevelWarning, messageTemplate, properties)
}

func (l *SeqLogger) Error(messageTemplate string, properties map[string]interface{}) {
	l.Log(LogLevelError, messageTemplate, properties)
}

func (l *SeqLogger) Fatal(messageTemplate string, properties map[string]interface{}) {
	l.Log(LogLevelFatal, messageTemplate, properties)
}

// Utility functions
func randomChoice(choices []string) string {
	return choices[rand.Intn(len(choices))]
}

func randomInt(min, max int) int {
	return rand.Intn(max-min+1) + min
}

func randomBool(probability float64) bool {
	return rand.Float64() < probability
}

// Workflow 1: 下注流程 (BettingWorkflow) - 8 步驟
func runBettingWorkflow(ctx context.Context, logger *SeqLogger) {
	for {
		select {
		case <-ctx.Done():
			return
		default:
			// 生成 workflow 的追蹤 ID
			traceID := uuid.New().String()
			correlationID := uuid.New().String()
			userID := fmt.Sprintf("USER_%d", randomInt(100, 999))
			sessionID := uuid.New().String()
			workflowName := "BettingWorkflow"

			baseContext := map[string]interface{}{
				"TraceId":      traceID,
				"CorrelationId": correlationID,
				"UserId":        userID,
				"SessionId":     sessionID,
				"WorkflowName":  workflowName,
			}

			// 步驟 1: 玩家登入
			loginContext := map[string]interface{}{
				"IP":       fmt.Sprintf("%d.%d.%d.%d", randomInt(1, 255), randomInt(1, 255), randomInt(1, 255), randomInt(1, 255)),
				"Device":   randomChoice([]string{"iOS", "Android", "Desktop", "Mobile Web"}),
				"Browser":  randomChoice([]string{"Chrome", "Safari", "Firefox", "Edge"}),
				"Location": randomChoice([]string{"台灣", "香港", "新加坡", "日本"}),
			}

			props := copyMap(baseContext)
			props["WorkflowStep"] = "1-Login"
			props["EventType"] = "PlayerLogin"
			props["LoginContext"] = loginContext
			props["Location"] = loginContext["Location"]
			props["Device"] = loginContext["Device"]
			logger.Info("玩家登入: {UserId} from {Location} using {Device}", props)

			time.Sleep(100 * time.Millisecond)

			// 步驟 2: 玩家驗證
			authDetails := map[string]interface{}{
				"AuthMethod":    randomChoice([]string{"Password", "Biometric", "2FA", "OAuth"}),
				"Token":         uuid.New().String(),
				"Role":          randomChoice([]string{"Player", "VIP", "Premium"}),
				"AuthTimestamp": time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "2-Authentication"
			props["EventType"] = "PlayerAuthenticated"
			props["AuthDetails"] = authDetails
			props["AuthMethod"] = authDetails["AuthMethod"]
			props["Role"] = authDetails["Role"]
			logger.Info("玩家驗證成功: {UserId} with {AuthMethod}, Role: {Role}", props)

			time.Sleep(100 * time.Millisecond)

			// 步驟 3: 查詢餘額
			currentBalance := randomInt(100, 10000)
			currency := randomChoice([]string{"USD", "TWD", "HKD", "SGD"})
			balanceInfo := map[string]interface{}{
				"Balance":     currentBalance,
				"Currency":    currency,
				"WalletId":    fmt.Sprintf("WALLET_%d", randomInt(1000, 9999)),
				"LastUpdated": time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "3-BalanceCheck"
			props["EventType"] = "BalanceChecked"
			props["BalanceInfo"] = balanceInfo
			props["Balance"] = currentBalance
			props["Currency"] = currency
			logger.Info("查詢餘額: {UserId} Balance: {Balance} {Currency}", props)

			// 低餘額警告 (20% 機率)
			if currentBalance < 500 && randomBool(0.2) {
				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-BalanceCheck"
				props["EventType"] = "BalanceChecked"
				props["BalanceInfo"] = balanceInfo
				props["Balance"] = currentBalance
				props["Currency"] = currency
				logger.Warning("低餘額警告: {UserId} 餘額僅剩 {Balance} {Currency}", props)
			}

			time.Sleep(100 * time.Millisecond)

			// 步驟 4: 遊戲開始
			gameID := fmt.Sprintf("GAME_%d", randomInt(1, 99))
			tableID := fmt.Sprintf("TABLE_%d", randomInt(1, 20))
			gameDetails := map[string]interface{}{
				"GameType": randomChoice([]string{"Baccarat", "BlackJack", "Roulette", "DragonTiger"}),
				"GameId":   gameID,
				"TableId":  tableID,
				"Dealer":   fmt.Sprintf("DEALER_%d", randomInt(1, 50)),
				"MinBet":   10,
				"MaxBet":   1000,
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "4-GameStart"
			props["EventType"] = "GameStarted"
			props["GameId"] = gameID
			props["TableId"] = tableID
			props["GameDetails"] = gameDetails
			props["GameType"] = gameDetails["GameType"]
			props["Dealer"] = gameDetails["Dealer"]
			logger.Info("遊戲開始: {GameType} at {TableId}, Dealer: {Dealer}", props)

			// 遊戲連線問題警告 (15% 機率)
			if randomBool(0.15) {
				latency := randomInt(500, 2000)
				connectionIssue := map[string]interface{}{
					"Issue":     "HighLatency",
					"Latency":   latency,
					"TableId":   tableID,
					"Timestamp": time.Now().UTC(),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "4-GameStart"
				props["EventType"] = "GameStarted"
				props["TableId"] = tableID
				props["ConnectionIssue"] = connectionIssue
				props["Latency"] = latency
				logger.Warning("遊戲連線延遲: {TableId} 延遲 {Latency}ms", props)
			}

			time.Sleep(200 * time.Millisecond)

			// 步驟 5: 下注
			betID := uuid.New().String()
			betAmount := randomInt(10, 500)

			// 檢查餘額是否足夠 (10% 機率不足)
			if randomBool(0.1) {
				betAmount = currentBalance + randomInt(100, 500) // 超過餘額
				insufficientError := map[string]interface{}{
					"BetId":            betID,
					"RequestedAmount":  betAmount,
					"AvailableBalance": currentBalance,
					"Shortage":         betAmount - currentBalance,
					"Currency":         currency,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "5-PlaceBet"
				props["EventType"] = "BetRejected"
				props["BetId"] = betID
				props["InsufficientError"] = insufficientError
				props["RequestedAmount"] = betAmount
				props["AvailableBalance"] = currentBalance
				logger.Error("下注失敗 - 餘額不足: {UserId} 嘗試下注 {RequestedAmount}，但餘額僅 {AvailableBalance}", props)

				props = copyMap(baseContext)
				logger.Warning("✗ BettingWorkflow 因餘額不足而終止 for {UserId} with TraceId: {TraceId}", props)

				time.Sleep(2 * time.Second)
				continue
			}

			// 檢查下注是否超過最大限制 (5% 機率)
			if betAmount > 1000 && randomBool(0.05) {
				limitError := map[string]interface{}{
					"BetId":           betID,
					"RequestedAmount": betAmount,
					"MaxLimit":        1000,
					"ExcessAmount":    betAmount - 1000,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "5-PlaceBet"
				props["EventType"] = "BetLimitExceeded"
				props["BetId"] = betID
				props["LimitError"] = limitError
				props["RequestedAmount"] = betAmount
				props["MaxLimit"] = 1000
				logger.Warning("下注警告 - 超過限額: {UserId} 嘗試下注 {RequestedAmount}，超過最大限額 {MaxLimit}", props)

				betAmount = 1000 // 調整為最大限額
			}

			betDetails := map[string]interface{}{
				"BetId":            betID,
				"Amount":           betAmount,
				"Currency":         currency,
				"BetType":          randomChoice([]string{"Player", "Banker", "Tie", "Red", "Black"}),
				"RemainingBalance": currentBalance - betAmount,
				"Timestamp":        time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "5-PlaceBet"
			props["EventType"] = "BetPlaced"
			props["BetId"] = betID
			props["BetDetails"] = betDetails
			props["Amount"] = betAmount
			props["Currency"] = currency
			props["BetType"] = betDetails["BetType"]
			logger.Info("下注: {UserId} bet {Amount} {Currency} on {BetType}", props)

			time.Sleep(300 * time.Millisecond)

			// 步驟 6: 遊戲結果
			gameRound := fmt.Sprintf("ROUND_%d", randomInt(10000, 99999))
			resultDetails := map[string]interface{}{
				"Result":    randomChoice([]string{"Player Win", "Banker Win", "Tie", "Red", "Black"}),
				"Cards":     fmt.Sprintf("%d-%d-%d", randomInt(1, 14), randomInt(1, 14), randomInt(1, 14)),
				"GameRound": gameRound,
				"Timestamp": time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "6-GameResult"
			props["EventType"] = "GameResult"
			props["ResultDetails"] = resultDetails
			props["Result"] = resultDetails["Result"]
			props["Cards"] = resultDetails["Cards"]
			props["GameRound"] = gameRound
			logger.Info("遊戲結果: {Result}, Cards: {Cards}, Round: {GameRound}", props)

			time.Sleep(100 * time.Millisecond)

			// 步驟 7: 注單結算
			isWin := randomBool(0.5)
			var winAmount int
			if isWin {
				winAmount = betAmount * randomInt(2, 5)
			} else {
				winAmount = 0
			}
			profit := winAmount - betAmount
			transactionID := uuid.New().String()

			status := "Loss"
			if isWin {
				status = "Win"
			}

			settlementDetails := map[string]interface{}{
				"BetId":         betID,
				"TransactionId": transactionID,
				"BetAmount":     betAmount,
				"WinAmount":     winAmount,
				"Profit":        profit,
				"Status":        status,
				"SettledAt":     time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "7-Settlement"
			props["EventType"] = "BetSettled"
			props["TransactionId"] = transactionID
			props["SettlementDetails"] = settlementDetails
			props["Status"] = status
			props["BetAmount"] = betAmount
			props["WinAmount"] = winAmount
			props["Profit"] = profit
			logger.Info("注單結算: {Status}, Bet: {BetAmount}, Win: {WinAmount}, Profit: {Profit}", props)

			// 結算延遲警告 (10% 機率)
			if randomBool(0.1) {
				delayInfo := map[string]interface{}{
					"TransactionId": transactionID,
					"DelaySeconds":  randomInt(3, 10),
					"Reason":        randomChoice([]string{"DatabaseLatency", "HighLoad", "NetworkIssue"}),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "7-Settlement"
				props["TransactionId"] = transactionID
				props["DelayInfo"] = delayInfo
				props["DelaySeconds"] = delayInfo["DelaySeconds"]
				props["Reason"] = delayInfo["Reason"]
				logger.Warning("結算延遲: Transaction {TransactionId} 延遲 {DelaySeconds} 秒，原因: {Reason}", props)
			}

			time.Sleep(100 * time.Millisecond)

			// 步驟 8: 餘額更新
			newBalance := currentBalance + profit
			changeType := "Debit"
			if profit >= 0 {
				changeType = "Credit"
			}

			balanceChange := map[string]interface{}{
				"PreviousBalance": currentBalance,
				"NewBalance":      newBalance,
				"ChangeAmount":    profit,
				"ChangeType":      changeType,
				"TransactionId":   transactionID,
				"Timestamp":       time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "8-BalanceUpdate"
			props["EventType"] = "BalanceUpdated"
			props["BalanceChange"] = balanceChange
			props["PreviousBalance"] = currentBalance
			props["NewBalance"] = newBalance
			props["ChangeType"] = changeType
			props["ChangeAmount"] = abs(profit)
			logger.Info("餘額更新: {PreviousBalance} -> {NewBalance} ({ChangeType}: {ChangeAmount})", props)

			// 餘額更新錯誤 (5% 機率)
			if randomBool(0.05) {
				updateError := map[string]interface{}{
					"TransactionId": transactionID,
					"ErrorCode":     "BALANCE_UPDATE_FAILED",
					"ErrorMessage":  "資料庫寫入失敗，將重試",
					"RetryAttempt":  1,
					"MaxRetries":    3,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "8-BalanceUpdate"
				props["TransactionId"] = transactionID
				props["UpdateError"] = updateError
				props["ErrorMessage"] = updateError["ErrorMessage"]
				logger.Error("餘額更新失敗: Transaction {TransactionId}，錯誤: {ErrorMessage}", props)
			}

			props = copyMap(baseContext)
			logger.Info("✓ BettingWorkflow completed for {UserId} with TraceId: {TraceId}", props)

			time.Sleep(2 * time.Second) // 每 2 秒執行一次
		}
	}
}

// Workflow 2: 存款流程 (DepositWorkflow) - 4 步驟
func runDepositWorkflow(ctx context.Context, logger *SeqLogger) {
	time.Sleep(500 * time.Millisecond) // 稍微延遲開始時間

	for {
		select {
		case <-ctx.Done():
			return
		default:
			traceID := uuid.New().String()
			correlationID := uuid.New().String()
			userID := fmt.Sprintf("USER_%d", randomInt(100, 999))
			sessionID := uuid.New().String()
			workflowName := "DepositWorkflow"

			baseContext := map[string]interface{}{
				"TraceId":       traceID,
				"CorrelationId": correlationID,
				"UserId":        userID,
				"SessionId":     sessionID,
				"WorkflowName":  workflowName,
			}

			// 步驟 1: 發起存款
			depositAmount := randomInt(100, 5000)
			depositRequest := map[string]interface{}{
				"Amount":        depositAmount,
				"Currency":      randomChoice([]string{"USD", "TWD", "HKD", "SGD"}),
				"PaymentMethod": randomChoice([]string{"CreditCard", "BankTransfer", "EWallet", "Crypto"}),
				"RequestedAt":   time.Now().UTC(),
			}

			props := copyMap(baseContext)
			props["WorkflowStep"] = "1-InitiateDeposit"
			props["EventType"] = "DepositInitiated"
			props["DepositRequest"] = depositRequest
			props["Amount"] = depositAmount
			props["Currency"] = depositRequest["Currency"]
			props["PaymentMethod"] = depositRequest["PaymentMethod"]
			logger.Info("發起存款: {UserId} requests {Amount} {Currency} via {PaymentMethod}", props)

			// 金額超過限制警告 (15% 機率)
			if depositAmount > 3000 && randomBool(0.15) {
				limitWarning := map[string]interface{}{
					"Amount":                        depositAmount,
					"DailyLimit":                    10000,
					"SingleTransactionLimit":        5000,
					"RequiresAdditionalVerification": depositAmount > 5000,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "1-InitiateDeposit"
				props["LimitWarning"] = limitWarning
				props["Amount"] = depositAmount
				props["RequiresAdditionalVerification"] = limitWarning["RequiresAdditionalVerification"]
				logger.Warning("存款金額警告: {UserId} 存款 {Amount} 接近限額，需要額外驗證: {RequiresAdditionalVerification}", props)
			}

			time.Sleep(100 * time.Millisecond)

			// 步驟 2: 驗證支付方式
			isValidationSuccess := randomBool(0.95) // 95% 成功率
			var failureReason interface{} = nil
			if !isValidationSuccess {
				failureReason = randomChoice([]string{"KYC_NOT_VERIFIED", "PAYMENT_METHOD_SUSPENDED", "ACCOUNT_RESTRICTED"})
			}

			validationDetails := map[string]interface{}{
				"IsValid":         isValidationSuccess,
				"ValidationRules": []string{"AmountLimit", "PaymentMethodActive", "KYCVerified"},
				"ProcessorId":     fmt.Sprintf("PROC_%d", randomInt(1, 10)),
				"ValidatedAt":     time.Now().UTC(),
				"FailureReason":   failureReason,
			}

			if isValidationSuccess {
				props = copyMap(baseContext)
				props["WorkflowStep"] = "2-ValidatePayment"
				props["EventType"] = "PaymentValidated"
				props["ValidationDetails"] = validationDetails
				props["PaymentMethod"] = depositRequest["PaymentMethod"]
				props["ProcessorId"] = validationDetails["ProcessorId"]
				logger.Info("驗證支付方式: {PaymentMethod} is valid, Processor: {ProcessorId}", props)
			} else {
				props = copyMap(baseContext)
				props["WorkflowStep"] = "2-ValidatePayment"
				props["EventType"] = "PaymentValidationFailed"
				props["ValidationDetails"] = validationDetails
				props["PaymentMethod"] = depositRequest["PaymentMethod"]
				props["FailureReason"] = failureReason
				logger.Error("驗證失敗: {UserId} 的支付方式 {PaymentMethod} 驗證失敗，原因: {FailureReason}", props)

				props = copyMap(baseContext)
				logger.Warning("✗ DepositWorkflow 因驗證失敗而終止 for {UserId} with TraceId: {TraceId}", props)

				time.Sleep(2 * time.Second)
				continue
			}

			time.Sleep(200 * time.Millisecond)

			// 步驟 3: 處理支付
			transactionID := uuid.New().String()

			// 支付處理器連線問題 (8% 機率)
			if randomBool(0.08) {
				connectionError := map[string]interface{}{
					"ProcessorId": validationDetails["ProcessorId"],
					"ErrorType":   "ConnectionTimeout",
					"RetryCount":  randomInt(1, 4),
					"Timestamp":   time.Now().UTC(),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-ProcessPayment"
				props["EventType"] = "PaymentProcessorConnectionError"
				props["TransactionId"] = transactionID
				props["ConnectionError"] = connectionError
				props["ProcessorId"] = validationDetails["ProcessorId"]
				props["RetryCount"] = connectionError["RetryCount"]
				logger.Warning("支付處理器連線問題: Processor {ProcessorId} 連線超時，重試次數: {RetryCount}", props)
			}

			isSuccess := randomBool(0.9) // 90% 成功率
			fee := float64(depositAmount) * 0.02 // 2% 手續費

			var errorCode interface{} = nil
			if !isSuccess {
				errorCode = randomChoice([]string{"INSUFFICIENT_FUNDS", "CARD_DECLINED", "BANK_REJECTION", "FRAUD_DETECTED"})
			}

			paymentStatus := "Failed"
			if isSuccess {
				paymentStatus = "Success"
			}

			paymentDetails := map[string]interface{}{
				"TransactionId": transactionID,
				"Status":        paymentStatus,
				"Amount":        depositAmount,
				"Fee":           fee,
				"NetAmount":     float64(depositAmount) - fee,
				"ProcessedAt":   time.Now().UTC(),
				"ErrorCode":     errorCode,
			}

			if isSuccess {
				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-ProcessPayment"
				props["EventType"] = "PaymentProcessed"
				props["TransactionId"] = transactionID
				props["PaymentDetails"] = paymentDetails
				props["Amount"] = depositAmount
				props["Fee"] = fee
				logger.Info("處理支付成功: Transaction {TransactionId}, Amount: {Amount}, Fee: {Fee}", props)
			} else {
				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-ProcessPayment"
				props["EventType"] = "PaymentFailed"
				props["TransactionId"] = transactionID
				props["PaymentDetails"] = paymentDetails
				props["ErrorCode"] = errorCode
				logger.Error("處理支付失敗: Transaction {TransactionId}, Error: {ErrorCode}", props)
			}

			time.Sleep(100 * time.Millisecond)

			// 步驟 4: 餘額入帳 (僅在成功時)
			if isSuccess {
				newBalance := randomInt(1000, 20000)
				creditDetails := map[string]interface{}{
					"Amount":      paymentDetails["NetAmount"],
					"TransactionId": transactionID,
					"NewBalance":  newBalance,
					"CreditedAt":  time.Now().UTC(),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "4-CreditBalance"
				props["EventType"] = "BalanceCredited"
				props["CreditDetails"] = creditDetails
				props["Amount"] = creditDetails["Amount"]
				props["NewBalance"] = newBalance
				logger.Info("餘額入帳: {Amount} credited, New Balance: {NewBalance}", props)
			}

			props = copyMap(baseContext)
			logger.Info("✓ DepositWorkflow completed for {UserId} with TraceId: {TraceId}", props)

			time.Sleep(2 * time.Second)
		}
	}
}

// Workflow 3: 提款流程 (WithdrawalWorkflow) - 3 步驟
func runWithdrawalWorkflow(ctx context.Context, logger *SeqLogger) {
	time.Sleep(1 * time.Second) // 稍微延遲開始時間

	for {
		select {
		case <-ctx.Done():
			return
		default:
			traceID := uuid.New().String()
			correlationID := uuid.New().String()
			userID := fmt.Sprintf("USER_%d", randomInt(100, 999))
			sessionID := uuid.New().String()
			workflowName := "WithdrawalWorkflow"

			baseContext := map[string]interface{}{
				"TraceId":       traceID,
				"CorrelationId": correlationID,
				"UserId":        userID,
				"SessionId":     sessionID,
				"WorkflowName":  workflowName,
			}

			// 步驟 1: 提款請求
			withdrawalAmount := randomInt(100, 3000)
			userBalance := randomInt(0, 5000)

			// 餘額不足錯誤 (12% 機率)
			if userBalance < withdrawalAmount && randomBool(0.12) {
				insufficientBalanceError := map[string]interface{}{
					"RequestedAmount":  withdrawalAmount,
					"AvailableBalance": userBalance,
					"Shortage":         withdrawalAmount - userBalance,
					"Currency":         randomChoice([]string{"USD", "TWD", "HKD", "SGD"}),
				}

				props := copyMap(baseContext)
				props["WorkflowStep"] = "1-RequestWithdrawal"
				props["EventType"] = "WithdrawalRejectedInsufficientBalance"
				props["InsufficientBalanceError"] = insufficientBalanceError
				props["RequestedAmount"] = withdrawalAmount
				props["AvailableBalance"] = userBalance
				logger.Error("提款失敗 - 餘額不足: {UserId} 請求提款 {RequestedAmount}，但餘額僅 {AvailableBalance}", props)

				props = copyMap(baseContext)
				logger.Warning("✗ WithdrawalWorkflow 因餘額不足而終止 for {UserId} with TraceId: {TraceId}", props)

				time.Sleep(2 * time.Second)
				continue
			}

			// 帳戶凍結錯誤 (5% 機率)
			if randomBool(0.05) {
				accountFrozenError := map[string]interface{}{
					"Reason":         randomChoice([]string{"SUSPECTED_FRAUD", "PENDING_INVESTIGATION", "COMPLIANCE_HOLD", "DUPLICATE_ACCOUNT"}),
					"FrozenSince":    time.Now().UTC().Add(-time.Duration(randomInt(1, 30)) * 24 * time.Hour),
					"ContactSupport": true,
				}

				props := copyMap(baseContext)
				props["WorkflowStep"] = "1-RequestWithdrawal"
				props["EventType"] = "WithdrawalRejectedAccountFrozen"
				props["AccountFrozenError"] = accountFrozenError
				props["Reason"] = accountFrozenError["Reason"]
				logger.Error("提款失敗 - 帳戶凍結: {UserId} 帳戶已凍結，原因: {Reason}", props)

				props = copyMap(baseContext)
				logger.Warning("✗ WithdrawalWorkflow 因帳戶凍結而終止 for {UserId} with TraceId: {TraceId}", props)

				time.Sleep(2 * time.Second)
				continue
			}

			withdrawalRequest := map[string]interface{}{
				"Amount":           withdrawalAmount,
				"Currency":         randomChoice([]string{"USD", "TWD", "HKD", "SGD"}),
				"WithdrawalMethod": randomChoice([]string{"BankTransfer", "EWallet", "Crypto", "Check"}),
				"AccountInfo":      fmt.Sprintf("****%d", randomInt(1000, 9999)),
				"RequestedAt":      time.Now().UTC(),
			}

			props := copyMap(baseContext)
			props["WorkflowStep"] = "1-RequestWithdrawal"
			props["EventType"] = "WithdrawalRequested"
			props["WithdrawalRequest"] = withdrawalRequest
			props["Amount"] = withdrawalAmount
			props["Currency"] = withdrawalRequest["Currency"]
			props["WithdrawalMethod"] = withdrawalRequest["WithdrawalMethod"]
			logger.Info("提款請求: {UserId} requests {Amount} {Currency} via {WithdrawalMethod}", props)

			// KYC 未完成警告 (10% 機率)
			if randomBool(0.1) {
				kycWarning := map[string]interface{}{
					"KYCStatus":        "Incomplete",
					"MissingDocuments": []string{"ID Verification", "Address Proof"},
					"RequiredForAmount": withdrawalAmount > 1000,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "1-RequestWithdrawal"
				props["KYCWarning"] = kycWarning
				logger.Warning("KYC 警告: {UserId} KYC 未完成，缺少文件，大額提款需要完成 KYC", props)
			}

			// 超過每日提款限制警告 (8% 機率)
			if withdrawalAmount > 2000 && randomBool(0.08) {
				todayWithdrawn := randomInt(1000, 3000)
				dailyLimitWarning := map[string]interface{}{
					"RequestedAmount": withdrawalAmount,
					"DailyLimit":      5000,
					"TodayWithdrawn":  todayWithdrawn,
					"RemainingLimit":  5000 - todayWithdrawn,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "1-RequestWithdrawal"
				props["DailyLimitWarning"] = dailyLimitWarning
				props["RequestedAmount"] = withdrawalAmount
				props["TodayWithdrawn"] = todayWithdrawn
				props["RemainingLimit"] = dailyLimitWarning["RemainingLimit"]
				logger.Warning("每日提款限額警告: {UserId} 請求 {RequestedAmount}，今日已提款 {TodayWithdrawn}，剩餘額度 {RemainingLimit}", props)
			}

			time.Sleep(150 * time.Millisecond)

			// 步驟 2: 風險評估
			riskScore := randomInt(0, 100)
			var riskLevel string
			if riskScore < 30 {
				riskLevel = "Low"
			} else if riskScore < 70 {
				riskLevel = "Medium"
			} else {
				riskLevel = "High"
			}
			riskPassed := riskScore < 70

			riskAssessment := map[string]interface{}{
				"RiskScore":  riskScore,
				"RiskLevel":  riskLevel,
				"Passed":     riskPassed,
				"Factors":    []string{"TransactionHistory", "AccountAge", "WithdrawalFrequency", "DepositWithdrawalRatio"},
				"AssessedAt": time.Now().UTC(),
			}

			props = copyMap(baseContext)
			props["WorkflowStep"] = "2-RiskAssessment"
			props["EventType"] = "RiskAssessed"
			props["RiskAssessment"] = riskAssessment
			props["RiskScore"] = riskScore
			props["RiskLevel"] = riskLevel
			props["Passed"] = riskPassed

			if riskLevel == "High" {
				logger.Warning("高風險提款: Score {RiskScore}, Level {RiskLevel}, 需要人工審核", props)
			} else if riskLevel == "Medium" {
				logger.Warning("中風險提款: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}", props)
			} else {
				logger.Info("風險評估: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}", props)
			}

			// 異常交易模式警告 (10% 機率)
			if randomBool(0.1) {
				patternWarning := map[string]interface{}{
					"Pattern":        randomChoice([]string{"FrequentWithdrawals", "LargeAmountAfterDeposit", "UnusualTiming", "NewDevice"}),
					"Confidence":     randomInt(60, 95),
					"RequiresReview": true,
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "2-RiskAssessment"
				props["PatternWarning"] = patternWarning
				props["Pattern"] = patternWarning["Pattern"]
				props["Confidence"] = patternWarning["Confidence"]
				logger.Warning("異常交易模式: {UserId} 偵測到 {Pattern}，信心度: {Confidence}%", props)
			}

			time.Sleep(200 * time.Millisecond)

			// 步驟 3: 核准或標記
			transactionID := uuid.New().String()

			if riskPassed {
				// 核准
				approvalDetails := map[string]interface{}{
					"TransactionId":           transactionID,
					"ApprovedBy":              "AutomatedSystem",
					"ApprovedAt":              time.Now().UTC(),
					"EstimatedCompletionTime": time.Now().UTC().Add(24 * time.Hour),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-Approval"
				props["EventType"] = "WithdrawalApproved"
				props["TransactionId"] = transactionID
				props["ApprovalDetails"] = approvalDetails
				logger.Info("提款核准: Transaction {TransactionId}, Estimated completion in 24h", props)
			} else {
				// 標記需要人工審核
				flagDetails := map[string]interface{}{
					"TransactionId":        transactionID,
					"Reason":               randomChoice([]string{"HighRiskScore", "UnusualPattern", "LargeAmount", "NewAccount"}),
					"RequiresManualReview": true,
					"FlaggedAt":            time.Now().UTC(),
					"ReviewerAssigned":     fmt.Sprintf("REVIEWER_%d", randomInt(1, 10)),
				}

				props = copyMap(baseContext)
				props["WorkflowStep"] = "3-FlaggedReview"
				props["EventType"] = "WithdrawalFlagged"
				props["TransactionId"] = transactionID
				props["FlagDetails"] = flagDetails
				props["Reason"] = flagDetails["Reason"]
				props["ReviewerAssigned"] = flagDetails["ReviewerAssigned"]
				logger.Warning("提款標記審核: Transaction {TransactionId}, Reason: {Reason}, Reviewer: {ReviewerAssigned}", props)
			}

			props = copyMap(baseContext)
			logger.Info("✓ WithdrawalWorkflow completed for {UserId} with TraceId: {TraceId}", props)

			time.Sleep(2 * time.Second)
		}
	}
}

// Helper function to copy map
func copyMap(m map[string]interface{}) map[string]interface{} {
	result := make(map[string]interface{})
	for k, v := range m {
		result[k] = v
	}
	return result
}

// Helper function for absolute value
func abs(n int) int {
	if n < 0 {
		return -n
	}
	return n
}

func main() {
	rand.Seed(time.Now().UnixNano())

	logger := NewSeqLogger()

	logger.Info("=== Go Seq Demo Started ===", map[string]interface{}{})
	logger.Info("Press Ctrl+C to stop", map[string]interface{}{})

	// 創建 context 用於優雅關閉
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	// 處理 SIGINT 和 SIGTERM 信號
	sigChan := make(chan os.Signal, 1)
	signal.Notify(sigChan, os.Interrupt, syscall.SIGTERM)

	// 使用 WaitGroup 等待所有 goroutine 完成
	var wg sync.WaitGroup

	// 啟動三個 workflow
	wg.Add(3)
	go func() {
		defer wg.Done()
		runBettingWorkflow(ctx, logger)
	}()
	go func() {
		defer wg.Done()
		runDepositWorkflow(ctx, logger)
	}()
	go func() {
		defer wg.Done()
		runWithdrawalWorkflow(ctx, logger)
	}()

	// 等待中斷信號
	<-sigChan
	logger.Info("Shutting down gracefully...", map[string]interface{}{})
	cancel()

	// 等待所有 workflow 停止
	wg.Wait()
	logger.Info("All workflows stopped", map[string]interface{}{})
}
