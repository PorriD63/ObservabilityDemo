const winston = require('winston');
const { v4: uuidv4 } = require('uuid');
const { SeqTransport } = require('@datalust/winston-seq');

// 定義微服務名稱
const SERVICE_PLAYER = 'PlayerService';
const SERVICE_GAME = 'GameService';
const SERVICE_WALLET = 'WalletService';
const SERVICE_PAYMENT = 'PaymentService';
const SERVICE_RISK = 'RiskService';
const SERVICE_NOTIFICATION = 'NotificationService';

// 配置 Winston + Seq
const logger = winston.createLogger({
  level: 'debug',
  format: winston.format.combine(
    winston.format.errors({ stack: true }),
    winston.format.json()
  ),
  defaultMeta: {
    Application: 'NodejsSeqDemo',
    Environment: 'Demo'
  },
  transports: [
    new winston.transports.Console({
      format: winston.format.combine(
        winston.format.colorize(),
        winston.format.printf(({ level, message, ServiceName, SourceContext, ...rest }) => {
          const ctx = ServiceName && SourceContext ? `[${ServiceName}/${SourceContext}]` : '';
          return `${level}: ${ctx} ${message}`;
        })
      )
    }),
    new SeqTransport({
      serverUrl: 'http://localhost:5341',
      onError: (e) => {
        console.error('Seq logging error:', e);
      }
    })
  ]
});

// 輔助函數：隨機選擇
const randomChoice = (arr) => arr[Math.floor(Math.random() * arr.length)];

// 輔助函數：隨機數
const randomInt = (min, max) => Math.floor(Math.random() * (max - min + 1)) + min;

// 輔助函數：隨機布林值
const randomBool = (probability = 0.5) => Math.random() < probability;

// 輔助函數：延遲
const delay = (ms) => new Promise(resolve => setTimeout(resolve, ms));

// Workflow 1: 下注流程 (BettingWorkflow) - 8 步驟
// 模擬跨服務調用: PlayerService -> WalletService -> GameService -> WalletService
async function runBettingWorkflow() {
  while (true) {
    try {
      // 生成 workflow 的追蹤 ID
      const traceId = uuidv4();
      const correlationId = uuidv4();
      const userId = `USER_${randomInt(100, 999)}`;
      const sessionId = uuidv4();
      const workflowName = 'BettingWorkflow';

      const baseContext = {
        TraceId: traceId,
        CorrelationId: correlationId,
        UserId: userId,
        SessionId: sessionId,
        WorkflowName: workflowName
      };

      // 步驟 1: 玩家登入 (PlayerService.AuthenticationHandler)
      const loginContext = {
        IP: `${randomInt(1, 255)}.${randomInt(1, 255)}.${randomInt(1, 255)}.${randomInt(1, 255)}`,
        Device: randomChoice(['iOS', 'Android', 'Desktop', 'Mobile Web']),
        Browser: randomChoice(['Chrome', 'Safari', 'Firefox', 'Edge']),
        Location: randomChoice(['台灣', '香港', '新加坡', '日本'])
      };

      logger.info('玩家登入: {UserId} from {Location} using {Device}', {
        ...baseContext,
        ServiceName: SERVICE_PLAYER,
        SourceContext: 'AuthenticationHandler',
        WorkflowStep: '1-Login',
        EventType: 'PlayerLogin',
        LoginContext: loginContext,
        Location: loginContext.Location,
        Device: loginContext.Device
      });

      await delay(100);

      // 步驟 2: 玩家驗證 (PlayerService.AuthorizationHandler)
      const authDetails = {
        AuthMethod: randomChoice(['Password', 'Biometric', '2FA', 'OAuth']),
        Token: uuidv4(),
        Role: randomChoice(['Player', 'VIP', 'Premium']),
        AuthTimestamp: new Date().toISOString()
      };

      logger.info('玩家驗證成功: {UserId} with {AuthMethod}, Role: {Role}', {
        ...baseContext,
        ServiceName: SERVICE_PLAYER,
        SourceContext: 'AuthorizationHandler',
        WorkflowStep: '2-Authentication',
        EventType: 'PlayerAuthenticated',
        AuthDetails: authDetails,
        AuthMethod: authDetails.AuthMethod,
        Role: authDetails.Role
      });

      await delay(100);

      // 步驟 3: 查詢餘額 (WalletService.BalanceManager)
      let currentBalance = randomInt(100, 10000);
      const currency = randomChoice(['USD', 'TWD', 'HKD', 'SGD']);
      const balanceInfo = {
        Balance: currentBalance,
        Currency: currency,
        WalletId: `WALLET_${randomInt(1000, 9999)}`,
        LastUpdated: new Date().toISOString()
      };

      logger.info('查詢餘額: {UserId} Balance: {Balance} {Currency}', {
        ...baseContext,
        ServiceName: SERVICE_WALLET,
        SourceContext: 'BalanceManager',
        WorkflowStep: '3-BalanceCheck',
        EventType: 'BalanceChecked',
        BalanceInfo: balanceInfo,
        Balance: currentBalance,
        Currency: currency
      });

      // 低餘額警告 (20% 機率)
      if (currentBalance < 500 && randomBool(0.2)) {
        logger.warn('低餘額警告: {UserId} 餘額僅剩 {Balance} {Currency}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'BalanceManager',
          WorkflowStep: '3-BalanceCheck',
          EventType: 'BalanceChecked',
          BalanceInfo: balanceInfo,
          Balance: currentBalance,
          Currency: currency
        });
      }

      await delay(100);

      // 步驟 4: 遊戲開始 (GameService.GameSessionManager)
      const gameId = `GAME_${randomInt(1, 99)}`;
      const tableId = `TABLE_${randomInt(1, 20)}`;
      const gameDetails = {
        GameType: randomChoice(['Baccarat', 'BlackJack', 'Roulette', 'DragonTiger']),
        GameId: gameId,
        TableId: tableId,
        Dealer: `DEALER_${randomInt(1, 50)}`,
        MinBet: 10,
        MaxBet: 1000
      };

      logger.info('遊戲開始: {GameType} at {TableId}, Dealer: {Dealer}', {
        ...baseContext,
        ServiceName: SERVICE_GAME,
        SourceContext: 'GameSessionManager',
        WorkflowStep: '4-GameStart',
        EventType: 'GameStarted',
        GameId: gameId,
        TableId: tableId,
        GameDetails: gameDetails,
        GameType: gameDetails.GameType,
        Dealer: gameDetails.Dealer
      });

      // 遊戲連線問題警告 (15% 機率)
      if (randomBool(0.15)) {
        const latency = randomInt(500, 2000);
        const connectionIssue = {
          Issue: 'HighLatency',
          Latency: latency,
          TableId: tableId,
          Timestamp: new Date().toISOString()
        };

        logger.warn('遊戲連線延遲: {TableId} 延遲 {Latency}ms', {
          ...baseContext,
          ServiceName: SERVICE_GAME,
          SourceContext: 'GameSessionManager',
          WorkflowStep: '4-GameStart',
          EventType: 'GameStarted',
          TableId: tableId,
          ConnectionIssue: connectionIssue,
          Latency: latency
        });
      }

      await delay(200);

      // 步驟 5: 下注
      const betId = uuidv4();
      let betAmount = randomInt(10, 500);

      // 檢查餘額是否足夠 (10% 機率不足) - WalletService 驗證
      if (randomBool(0.1)) {
        betAmount = currentBalance + randomInt(100, 500); // 超過餘額
        const insufficientError = {
          BetId: betId,
          RequestedAmount: betAmount,
          AvailableBalance: currentBalance,
          Shortage: betAmount - currentBalance,
          Currency: currency
        };

        logger.error('下注失敗 - 餘額不足: {UserId} 嘗試下注 {RequestedAmount}，但餘額僅 {AvailableBalance}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'BalanceValidator',
          WorkflowStep: '5-PlaceBet',
          EventType: 'BetRejected',
          BetId: betId,
          InsufficientError: insufficientError,
          RequestedAmount: betAmount,
          AvailableBalance: currentBalance
        });

        // 發送通知 (NotificationService)
        logger.warn('BettingWorkflow 因餘額不足而終止 for {UserId} with TraceId: {TraceId}', {
          ...baseContext,
          ServiceName: SERVICE_NOTIFICATION,
          SourceContext: 'AlertDispatcher'
        });

        await delay(2000);
        continue;
      }

      // 檢查下注是否超過最大限制 (5% 機率) - GameService 驗證
      if (betAmount > 1000 && randomBool(0.05)) {
        const limitError = {
          BetId: betId,
          RequestedAmount: betAmount,
          MaxLimit: 1000,
          ExcessAmount: betAmount - 1000
        };

        logger.warn('下注警告 - 超過限額: {UserId} 嘗試下注 {RequestedAmount}，超過最大限額 {MaxLimit}', {
          ...baseContext,
          ServiceName: SERVICE_GAME,
          SourceContext: 'BettingLimitValidator',
          WorkflowStep: '5-PlaceBet',
          EventType: 'BetLimitExceeded',
          BetId: betId,
          LimitError: limitError,
          RequestedAmount: betAmount,
          MaxLimit: 1000
        });

        betAmount = 1000; // 調整為最大限額
      }

      const betDetails = {
        BetId: betId,
        Amount: betAmount,
        Currency: currency,
        BetType: randomChoice(['Player', 'Banker', 'Tie', 'Red', 'Black']),
        RemainingBalance: currentBalance - betAmount,
        Timestamp: new Date().toISOString()
      };

      logger.info('下注: {UserId} bet {Amount} {Currency} on {BetType}', {
        ...baseContext,
        ServiceName: SERVICE_GAME,
        SourceContext: 'BettingHandler',
        WorkflowStep: '5-PlaceBet',
        EventType: 'BetPlaced',
        BetId: betId,
        BetDetails: betDetails,
        Amount: betAmount,
        Currency: currency,
        BetType: betDetails.BetType
      });

      await delay(300);

      // 步驟 6: 遊戲結果 (GameService.ResultHandler)
      const gameRound = `ROUND_${randomInt(10000, 99999)}`;
      const resultDetails = {
        Result: randomChoice(['Player Win', 'Banker Win', 'Tie', 'Red', 'Black']),
        Cards: `${randomInt(1, 14)}-${randomInt(1, 14)}-${randomInt(1, 14)}`,
        GameRound: gameRound,
        Timestamp: new Date().toISOString()
      };

      logger.info('遊戲結果: {Result}, Cards: {Cards}, Round: {GameRound}', {
        ...baseContext,
        ServiceName: SERVICE_GAME,
        SourceContext: 'ResultHandler',
        WorkflowStep: '6-GameResult',
        EventType: 'GameResult',
        ResultDetails: resultDetails,
        Result: resultDetails.Result,
        Cards: resultDetails.Cards,
        GameRound: gameRound
      });

      await delay(100);

      // 步驟 7: 注單結算 (WalletService.SettlementProcessor)
      const isWin = randomBool();
      const winAmount = isWin ? betAmount * randomInt(2, 5) : 0;
      const profit = winAmount - betAmount;
      const transactionId = uuidv4();

      const settlementDetails = {
        BetId: betId,
        TransactionId: transactionId,
        BetAmount: betAmount,
        WinAmount: winAmount,
        Profit: profit,
        Status: isWin ? 'Win' : 'Loss',
        SettledAt: new Date().toISOString()
      };

      logger.info('注單結算: {Status}, Bet: {BetAmount}, Win: {WinAmount}, Profit: {Profit}', {
        ...baseContext,
        ServiceName: SERVICE_WALLET,
        SourceContext: 'SettlementProcessor',
        WorkflowStep: '7-Settlement',
        EventType: 'BetSettled',
        TransactionId: transactionId,
        SettlementDetails: settlementDetails,
        Status: settlementDetails.Status,
        BetAmount: betAmount,
        WinAmount: winAmount,
        Profit: profit
      });

      // 結算延遲警告 (10% 機率)
      if (randomBool(0.1)) {
        const delayInfo = {
          TransactionId: transactionId,
          DelaySeconds: randomInt(3, 10),
          Reason: randomChoice(['DatabaseLatency', 'HighLoad', 'NetworkIssue'])
        };

        logger.warn('結算延遲: Transaction {TransactionId} 延遲 {DelaySeconds} 秒，原因: {Reason}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'SettlementProcessor',
          WorkflowStep: '7-Settlement',
          TransactionId: transactionId,
          DelayInfo: delayInfo,
          DelaySeconds: delayInfo.DelaySeconds,
          Reason: delayInfo.Reason
        });
      }

      await delay(100);

      // 步驟 8: 餘額更新 (WalletService.BalanceManager)
      const newBalance = currentBalance + profit;
      const balanceChange = {
        PreviousBalance: currentBalance,
        NewBalance: newBalance,
        ChangeAmount: profit,
        ChangeType: profit >= 0 ? 'Credit' : 'Debit',
        TransactionId: transactionId,
        Timestamp: new Date().toISOString()
      };

      logger.info('餘額更新: {PreviousBalance} -> {NewBalance} ({ChangeType}: {ChangeAmount})', {
        ...baseContext,
        ServiceName: SERVICE_WALLET,
        SourceContext: 'BalanceManager',
        WorkflowStep: '8-BalanceUpdate',
        EventType: 'BalanceUpdated',
        BalanceChange: balanceChange,
        PreviousBalance: currentBalance,
        NewBalance: newBalance,
        ChangeType: balanceChange.ChangeType,
        ChangeAmount: Math.abs(profit)
      });

      // 餘額更新錯誤 (5% 機率)
      if (randomBool(0.05)) {
        const updateError = {
          TransactionId: transactionId,
          ErrorCode: 'BALANCE_UPDATE_FAILED',
          ErrorMessage: '資料庫寫入失敗，將重試',
          RetryAttempt: 1,
          MaxRetries: 3
        };

        logger.error('餘額更新失敗: Transaction {TransactionId}，錯誤: {ErrorMessage}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'BalanceManager',
          WorkflowStep: '8-BalanceUpdate',
          TransactionId: transactionId,
          UpdateError: updateError,
          ErrorMessage: updateError.ErrorMessage
        });
      }

      // 完成通知 (NotificationService)
      logger.info('BettingWorkflow completed for {UserId} with TraceId: {TraceId}', {
        ...baseContext,
        ServiceName: SERVICE_NOTIFICATION,
        SourceContext: 'WorkflowNotifier'
      });

      await delay(2000); // 每 2 秒執行一次
    } catch (error) {
      logger.error('Error in BettingWorkflow', { error: error.message, stack: error.stack });
      await delay(2000);
    }
  }
}

// Workflow 2: 存款流程 (DepositWorkflow) - 4 步驟
// 模擬跨服務調用: WalletService -> PaymentService -> WalletService
async function runDepositWorkflow() {
  await delay(500); // 稍微延遲開始時間

  while (true) {
    try {
      const traceId = uuidv4();
      const correlationId = uuidv4();
      const userId = `USER_${randomInt(100, 999)}`;
      const sessionId = uuidv4();
      const workflowName = 'DepositWorkflow';

      const baseContext = {
        TraceId: traceId,
        CorrelationId: correlationId,
        UserId: userId,
        SessionId: sessionId,
        WorkflowName: workflowName
      };

      // 步驟 1: 發起存款 (WalletService.DepositRequestHandler)
      const depositAmount = randomInt(100, 5000);
      const depositRequest = {
        Amount: depositAmount,
        Currency: randomChoice(['USD', 'TWD', 'HKD', 'SGD']),
        PaymentMethod: randomChoice(['CreditCard', 'BankTransfer', 'EWallet', 'Crypto']),
        RequestedAt: new Date().toISOString()
      };

      logger.info('發起存款: {UserId} requests {Amount} {Currency} via {PaymentMethod}', {
        ...baseContext,
        ServiceName: SERVICE_WALLET,
        SourceContext: 'DepositRequestHandler',
        WorkflowStep: '1-InitiateDeposit',
        EventType: 'DepositInitiated',
        DepositRequest: depositRequest,
        Amount: depositAmount,
        Currency: depositRequest.Currency,
        PaymentMethod: depositRequest.PaymentMethod
      });

      // 金額超過限制警告 (15% 機率) - RiskService 檢查
      if (depositAmount > 3000 && randomBool(0.15)) {
        const limitWarning = {
          Amount: depositAmount,
          DailyLimit: 10000,
          SingleTransactionLimit: 5000,
          RequiresAdditionalVerification: depositAmount > 5000
        };

        logger.warn('存款金額警告: {UserId} 存款 {Amount} 接近限額，需要額外驗證: {RequiresAdditionalVerification}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'TransactionLimitChecker',
          WorkflowStep: '1-InitiateDeposit',
          LimitWarning: limitWarning,
          Amount: depositAmount,
          RequiresAdditionalVerification: limitWarning.RequiresAdditionalVerification
        });
      }

      await delay(100);

      // 步驟 2: 驗證支付方式 (PaymentService.PaymentValidator)
      const isValidationSuccess = randomBool(0.95); // 95% 成功率
      const validationDetails = {
        IsValid: isValidationSuccess,
        ValidationRules: ['AmountLimit', 'PaymentMethodActive', 'KYCVerified'],
        ProcessorId: `PROC_${randomInt(1, 10)}`,
        ValidatedAt: new Date().toISOString(),
        FailureReason: isValidationSuccess ? null : randomChoice(['KYC_NOT_VERIFIED', 'PAYMENT_METHOD_SUSPENDED', 'ACCOUNT_RESTRICTED'])
      };

      if (isValidationSuccess) {
        logger.info('驗證支付方式: {PaymentMethod} is valid, Processor: {ProcessorId}', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'PaymentValidator',
          WorkflowStep: '2-ValidatePayment',
          EventType: 'PaymentValidated',
          ValidationDetails: validationDetails,
          PaymentMethod: depositRequest.PaymentMethod,
          ProcessorId: validationDetails.ProcessorId
        });
      } else {
        logger.error('驗證失敗: {UserId} 的支付方式 {PaymentMethod} 驗證失敗，原因: {FailureReason}', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'PaymentValidator',
          WorkflowStep: '2-ValidatePayment',
          EventType: 'PaymentValidationFailed',
          ValidationDetails: validationDetails,
          PaymentMethod: depositRequest.PaymentMethod,
          FailureReason: validationDetails.FailureReason
        });

        // 發送通知 (NotificationService)
        logger.warn('DepositWorkflow 因驗證失敗而終止 for {UserId} with TraceId: {TraceId}', {
          ...baseContext,
          ServiceName: SERVICE_NOTIFICATION,
          SourceContext: 'AlertDispatcher'
        });

        await delay(2000);
        continue;
      }

      await delay(200);

      // 步驟 3: 處理支付 (PaymentService.PaymentProcessor)
      const transactionId = uuidv4();

      // 支付處理器連線問題 (8% 機率)
      if (randomBool(0.08)) {
        const connectionError = {
          ProcessorId: validationDetails.ProcessorId,
          ErrorType: 'ConnectionTimeout',
          RetryCount: randomInt(1, 4),
          Timestamp: new Date().toISOString()
        };

        logger.warn('支付處理器連線問題: Processor {ProcessorId} 連線超時，重試次數: {RetryCount}', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'PaymentGatewayClient',
          WorkflowStep: '3-ProcessPayment',
          EventType: 'PaymentProcessorConnectionError',
          TransactionId: transactionId,
          ConnectionError: connectionError,
          ProcessorId: validationDetails.ProcessorId,
          RetryCount: connectionError.RetryCount
        });
      }

      const isSuccess = randomBool(0.9); // 90% 成功率
      const fee = depositAmount * 0.02; // 2% 手續費

      const paymentDetails = {
        TransactionId: transactionId,
        Status: isSuccess ? 'Success' : 'Failed',
        Amount: depositAmount,
        Fee: fee,
        NetAmount: depositAmount - fee,
        ProcessedAt: new Date().toISOString(),
        ErrorCode: isSuccess ? null : randomChoice(['INSUFFICIENT_FUNDS', 'CARD_DECLINED', 'BANK_REJECTION', 'FRAUD_DETECTED'])
      };

      if (isSuccess) {
        logger.info('處理支付成功: Transaction {TransactionId}, Amount: {Amount}, Fee: {Fee}', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'PaymentProcessor',
          WorkflowStep: '3-ProcessPayment',
          EventType: 'PaymentProcessed',
          TransactionId: transactionId,
          PaymentDetails: paymentDetails,
          Amount: depositAmount,
          Fee: fee
        });
      } else {
        logger.error('處理支付失敗: Transaction {TransactionId}, Error: {ErrorCode}', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'PaymentProcessor',
          WorkflowStep: '3-ProcessPayment',
          EventType: 'PaymentFailed',
          TransactionId: transactionId,
          PaymentDetails: paymentDetails,
          ErrorCode: paymentDetails.ErrorCode
        });
      }

      await delay(100);

      // 步驟 4: 餘額入帳 (WalletService.BalanceManager) - 僅在成功時
      if (isSuccess) {
        const newBalance = randomInt(1000, 20000);
        const creditDetails = {
          Amount: paymentDetails.NetAmount,
          TransactionId: transactionId,
          NewBalance: newBalance,
          CreditedAt: new Date().toISOString()
        };

        logger.info('餘額入帳: {Amount} credited, New Balance: {NewBalance}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'BalanceManager',
          WorkflowStep: '4-CreditBalance',
          EventType: 'BalanceCredited',
          CreditDetails: creditDetails,
          Amount: creditDetails.Amount,
          NewBalance: newBalance
        });
      }

      // 完成通知 (NotificationService)
      logger.info('DepositWorkflow completed for {UserId} with TraceId: {TraceId}', {
        ...baseContext,
        ServiceName: SERVICE_NOTIFICATION,
        SourceContext: 'WorkflowNotifier'
      });

      await delay(2000);
    } catch (error) {
      logger.error('Error in DepositWorkflow', { error: error.message, stack: error.stack });
      await delay(2000);
    }
  }
}

// Workflow 3: 提款流程 (WithdrawalWorkflow) - 3 步驟
// 模擬跨服務調用: WalletService -> RiskService -> PaymentService
async function runWithdrawalWorkflow() {
  await delay(1000); // 稍微延遲開始時間

  while (true) {
    try {
      const traceId = uuidv4();
      const correlationId = uuidv4();
      const userId = `USER_${randomInt(100, 999)}`;
      const sessionId = uuidv4();
      const workflowName = 'WithdrawalWorkflow';

      const baseContext = {
        TraceId: traceId,
        CorrelationId: correlationId,
        UserId: userId,
        SessionId: sessionId,
        WorkflowName: workflowName
      };

      // 步驟 1: 提款請求
      const withdrawalAmount = randomInt(100, 3000);
      const userBalance = randomInt(0, 5000);

      // 餘額不足錯誤 (12% 機率) - WalletService.BalanceValidator
      if (userBalance < withdrawalAmount && randomBool(0.12)) {
        const insufficientBalanceError = {
          RequestedAmount: withdrawalAmount,
          AvailableBalance: userBalance,
          Shortage: withdrawalAmount - userBalance,
          Currency: randomChoice(['USD', 'TWD', 'HKD', 'SGD'])
        };

        logger.error('提款失敗 - 餘額不足: {UserId} 請求提款 {RequestedAmount}，但餘額僅 {AvailableBalance}', {
          ...baseContext,
          ServiceName: SERVICE_WALLET,
          SourceContext: 'BalanceValidator',
          WorkflowStep: '1-RequestWithdrawal',
          EventType: 'WithdrawalRejectedInsufficientBalance',
          InsufficientBalanceError: insufficientBalanceError,
          RequestedAmount: withdrawalAmount,
          AvailableBalance: userBalance
        });

        // 發送通知 (NotificationService)
        logger.warn('WithdrawalWorkflow 因餘額不足而終止 for {UserId} with TraceId: {TraceId}', {
          ...baseContext,
          ServiceName: SERVICE_NOTIFICATION,
          SourceContext: 'AlertDispatcher'
        });

        await delay(2000);
        continue;
      }

      // 帳戶凍結錯誤 (5% 機率) - RiskService.AccountStatusChecker
      if (randomBool(0.05)) {
        const accountFrozenError = {
          Reason: randomChoice(['SUSPECTED_FRAUD', 'PENDING_INVESTIGATION', 'COMPLIANCE_HOLD', 'DUPLICATE_ACCOUNT']),
          FrozenSince: new Date(Date.now() - randomInt(1, 30) * 24 * 60 * 60 * 1000).toISOString(),
          ContactSupport: true
        };

        logger.error('提款失敗 - 帳戶凍結: {UserId} 帳戶已凍結，原因: {Reason}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'AccountStatusChecker',
          WorkflowStep: '1-RequestWithdrawal',
          EventType: 'WithdrawalRejectedAccountFrozen',
          AccountFrozenError: accountFrozenError,
          Reason: accountFrozenError.Reason
        });

        // 發送通知 (NotificationService)
        logger.warn('WithdrawalWorkflow 因帳戶凍結而終止 for {UserId} with TraceId: {TraceId}', {
          ...baseContext,
          ServiceName: SERVICE_NOTIFICATION,
          SourceContext: 'AlertDispatcher'
        });

        await delay(2000);
        continue;
      }

      const withdrawalRequest = {
        Amount: withdrawalAmount,
        Currency: randomChoice(['USD', 'TWD', 'HKD', 'SGD']),
        WithdrawalMethod: randomChoice(['BankTransfer', 'EWallet', 'Crypto', 'Check']),
        AccountInfo: `****${randomInt(1000, 9999)}`,
        RequestedAt: new Date().toISOString()
      };

      // WalletService.WithdrawalRequestHandler
      logger.info('提款請求: {UserId} requests {Amount} {Currency} via {WithdrawalMethod}', {
        ...baseContext,
        ServiceName: SERVICE_WALLET,
        SourceContext: 'WithdrawalRequestHandler',
        WorkflowStep: '1-RequestWithdrawal',
        EventType: 'WithdrawalRequested',
        WithdrawalRequest: withdrawalRequest,
        Amount: withdrawalAmount,
        Currency: withdrawalRequest.Currency,
        WithdrawalMethod: withdrawalRequest.WithdrawalMethod
      });

      // KYC 未完成警告 (10% 機率) - PlayerService.KYCValidator
      if (randomBool(0.1)) {
        const kycWarning = {
          KYCStatus: 'Incomplete',
          MissingDocuments: ['ID Verification', 'Address Proof'],
          RequiredForAmount: withdrawalAmount > 1000
        };

        logger.warn('KYC 警告: {UserId} KYC 未完成，缺少文件，大額提款需要完成 KYC', {
          ...baseContext,
          ServiceName: SERVICE_PLAYER,
          SourceContext: 'KYCValidator',
          WorkflowStep: '1-RequestWithdrawal',
          KYCWarning: kycWarning
        });
      }

      // 超過每日提款限制警告 (8% 機率) - RiskService.TransactionLimitChecker
      if (withdrawalAmount > 2000 && randomBool(0.08)) {
        const dailyLimitWarning = {
          RequestedAmount: withdrawalAmount,
          DailyLimit: 5000,
          TodayWithdrawn: randomInt(1000, 3000),
          RemainingLimit: 5000 - randomInt(1000, 3000)
        };

        logger.warn('每日提款限額警告: {UserId} 請求 {RequestedAmount}，今日已提款 {TodayWithdrawn}，剩餘額度 {RemainingLimit}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'TransactionLimitChecker',
          WorkflowStep: '1-RequestWithdrawal',
          DailyLimitWarning: dailyLimitWarning,
          RequestedAmount: withdrawalAmount,
          TodayWithdrawn: dailyLimitWarning.TodayWithdrawn,
          RemainingLimit: dailyLimitWarning.RemainingLimit
        });
      }

      await delay(150);

      // 步驟 2: 風險評估 (RiskService.RiskAssessmentEngine)
      const riskScore = randomInt(0, 100);
      const riskLevel = riskScore < 30 ? 'Low' : riskScore < 70 ? 'Medium' : 'High';
      const riskPassed = riskScore < 70;

      const riskAssessment = {
        RiskScore: riskScore,
        RiskLevel: riskLevel,
        Passed: riskPassed,
        Factors: ['TransactionHistory', 'AccountAge', 'WithdrawalFrequency', 'DepositWithdrawalRatio'],
        AssessedAt: new Date().toISOString()
      };

      if (riskLevel === 'High') {
        logger.warn('高風險提款: Score {RiskScore}, Level {RiskLevel}, 需要人工審核', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'RiskAssessmentEngine',
          WorkflowStep: '2-RiskAssessment',
          EventType: 'RiskAssessed',
          RiskAssessment: riskAssessment,
          RiskScore: riskScore,
          RiskLevel: riskLevel
        });
      } else if (riskLevel === 'Medium') {
        logger.warn('中風險提款: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'RiskAssessmentEngine',
          WorkflowStep: '2-RiskAssessment',
          EventType: 'RiskAssessed',
          RiskAssessment: riskAssessment,
          RiskScore: riskScore,
          RiskLevel: riskLevel,
          Passed: riskPassed
        });
      } else {
        logger.info('風險評估: Score {RiskScore}, Level {RiskLevel}, Passed: {Passed}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'RiskAssessmentEngine',
          WorkflowStep: '2-RiskAssessment',
          EventType: 'RiskAssessed',
          RiskAssessment: riskAssessment,
          RiskScore: riskScore,
          RiskLevel: riskLevel,
          Passed: riskPassed
        });
      }

      // 異常交易模式警告 (10% 機率) - RiskService.PatternDetector
      if (randomBool(0.1)) {
        const patternWarning = {
          Pattern: randomChoice(['FrequentWithdrawals', 'LargeAmountAfterDeposit', 'UnusualTiming', 'NewDevice']),
          Confidence: randomInt(60, 95),
          RequiresReview: true
        };

        logger.warn('異常交易模式: {UserId} 偵測到 {Pattern}，信心度: {Confidence}%', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'PatternDetector',
          WorkflowStep: '2-RiskAssessment',
          PatternWarning: patternWarning,
          Pattern: patternWarning.Pattern,
          Confidence: patternWarning.Confidence
        });
      }

      await delay(200);

      // 步驟 3: 核准或標記
      const transactionId = uuidv4();

      if (riskPassed) {
        // 核准 (PaymentService.WithdrawalApprover)
        const approvalDetails = {
          TransactionId: transactionId,
          ApprovedBy: 'AutomatedSystem',
          ApprovedAt: new Date().toISOString(),
          EstimatedCompletionTime: new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString()
        };

        logger.info('提款核准: Transaction {TransactionId}, Estimated completion in 24h', {
          ...baseContext,
          ServiceName: SERVICE_PAYMENT,
          SourceContext: 'WithdrawalApprover',
          WorkflowStep: '3-Approval',
          EventType: 'WithdrawalApproved',
          TransactionId: transactionId,
          ApprovalDetails: approvalDetails
        });
      } else {
        // 標記需要人工審核 (RiskService.ManualReviewQueue)
        const flagDetails = {
          TransactionId: transactionId,
          Reason: randomChoice(['HighRiskScore', 'UnusualPattern', 'LargeAmount', 'NewAccount']),
          RequiresManualReview: true,
          FlaggedAt: new Date().toISOString(),
          ReviewerAssigned: `REVIEWER_${randomInt(1, 10)}`
        };

        logger.warn('提款標記審核: Transaction {TransactionId}, Reason: {Reason}, Reviewer: {ReviewerAssigned}', {
          ...baseContext,
          ServiceName: SERVICE_RISK,
          SourceContext: 'ManualReviewQueue',
          WorkflowStep: '3-FlaggedReview',
          EventType: 'WithdrawalFlagged',
          TransactionId: transactionId,
          FlagDetails: flagDetails,
          Reason: flagDetails.Reason,
          ReviewerAssigned: flagDetails.ReviewerAssigned
        });
      }

      // 完成通知 (NotificationService)
      logger.info('WithdrawalWorkflow completed for {UserId} with TraceId: {TraceId}', {
        ...baseContext,
        ServiceName: SERVICE_NOTIFICATION,
        SourceContext: 'WorkflowNotifier'
      });

      await delay(2000);
    } catch (error) {
      logger.error('Error in WithdrawalWorkflow', { error: error.message, stack: error.stack });
      await delay(2000);
    }
  }
}

// 主程式
async function main() {
  logger.info('=== Node.js Seq Demo Started ===');
  logger.info('Press Ctrl+C to stop');

  // 啟動三個 workflow
  Promise.all([
    runBettingWorkflow(),
    runDepositWorkflow(),
    runWithdrawalWorkflow()
  ]).catch(error => {
    logger.error('Fatal error', { error: error.message, stack: error.stack });
    process.exit(1);
  });

  // 處理優雅關閉
  process.on('SIGINT', () => {
    logger.info('Shutting down gracefully...');
    process.exit(0);
  });

  process.on('SIGTERM', () => {
    logger.info('Shutting down gracefully...');
    process.exit(0);
  });
}

main();
