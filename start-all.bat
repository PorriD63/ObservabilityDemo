@echo off
echo ========================================
echo Starting Game Platform Observability Demo
echo ========================================
echo.

echo [1/6] Starting infrastructure (Docker Compose)...
docker compose up -d
echo Waiting for infrastructure to be ready...
timeout /t 30 /nobreak > nul
echo Infrastructure is running.
echo   Seq:       http://localhost:5341
echo   Grafana:   http://localhost:3000
echo   Kafka UI:  http://localhost:8080
echo.

echo [2/6] Starting FinanceService (port 5300)...
start "FinanceService" cmd /c "cd /d %~dp0src\ObservabilityDemo.FinanceService && dotnet run"
timeout /t 3 /nobreak > nul
echo.

echo [3/6] Starting PlayerGameService (port 5200)...
start "PlayerGameService" cmd /c "cd /d %~dp0src\ObservabilityDemo.PlayerGameService && dotnet run"
timeout /t 3 /nobreak > nul
echo.

echo [4/6] Starting GatewayService (port 5100)...
start "GatewayService" cmd /c "cd /d %~dp0src\ObservabilityDemo.GatewayService && dotnet run"
timeout /t 2 /nobreak > nul
echo.

echo [5/6] Starting NotificationService (Kafka consumer)...
start "NotificationService" cmd /c "cd /d %~dp0src\ObservabilityDemo.NotificationService && dotnet run"
echo.

echo [6/6] All services started!
echo.
echo   FinanceService:       port 5300 (gRPC server)
echo   PlayerGameService:    port 5200 (gRPC server)
echo   GatewayService:       port 5100 (gRPC client / workflow orchestrator)
echo   NotificationService:  Kafka consumer
echo.
echo   Seq:       http://localhost:5341
echo   Grafana:   http://localhost:3000
echo   Kafka UI:  http://localhost:8080
echo.
echo Press any key to stop all services...
pause > nul

echo.
echo Stopping services...
taskkill /FI "WINDOWTITLE eq FinanceService" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq PlayerGameService" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq GatewayService" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq NotificationService" /T /F >nul 2>&1
echo Stopping Docker Compose...
docker compose down -v
echo Done!
