#!/bin/bash

echo "========================================"
echo "Starting Seq Multi-Language Demo"
echo "========================================"
echo ""

echo "[1/4] Starting Seq service..."
docker-compose up -d
echo "Waiting for Seq to be ready..."
sleep 10
echo "Seq is running at http://localhost:5341"
echo ""

echo "[2/4] You can now start the demo applications:"
echo ""
echo "  Go:      cd golang-demo && go run main.go"
echo "  Node.js: cd nodejs-demo && npm start"
echo "  .NET:    cd dotnet-demo && dotnet run"
echo ""

echo "[3/4] Opening Seq Web UI..."
if command -v xdg-open > /dev/null; then
    xdg-open http://localhost:5341
elif command -v open > /dev/null; then
    open http://localhost:5341
fi
echo ""

echo "[4/4] Press Ctrl+C to stop Seq when you're done..."
read -p "Press Enter to stop Seq..."

echo ""
echo "Stopping Seq..."
docker-compose down
echo "Done!"
