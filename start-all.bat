@echo off
echo ========================================
echo Starting Seq Multi-Language Demo
echo ========================================
echo.

echo [1/4] Starting Seq service...
docker-compose up -d
echo Waiting for Seq to be ready...
timeout /t 10 /nobreak > nul
echo Seq is running at http://localhost:5341
echo.

echo [2/4] You can now start the demo applications:
echo.
echo   Go:      cd golang-demo ^&^& go run main.go
echo   Node.js: cd nodejs-demo ^&^& npm start
echo   .NET:    cd dotnet-demo ^&^& dotnet run
echo.

echo [3/4] Open Seq Web UI:
start http://localhost:5341
echo.

echo [4/4] Press any key to stop Seq when you're done...
pause > nul

echo.
echo Stopping Seq...
docker-compose down
echo Done!
