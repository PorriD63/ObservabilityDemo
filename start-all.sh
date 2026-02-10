#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PIDS=()

cleanup() {
    echo ""
    echo "Stopping services..."
    for pid in "${PIDS[@]}"; do
        kill "$pid" 2>/dev/null
    done
    echo "Stopping Docker Compose..."
    docker compose down
    echo "Done!"
    exit 0
}

trap cleanup SIGINT SIGTERM

echo "========================================"
echo "Starting Game Platform Observability Demo"
echo "========================================"
echo ""

echo "[1/6] Starting infrastructure (Docker Compose)..."
docker compose up -d
echo "Waiting for infrastructure to be ready..."
sleep 30
echo "Infrastructure is running."
echo "  Seq:      http://localhost:5341"
echo "  Grafana:  http://localhost:3000"
echo "  Kafka UI: http://localhost:8080"
echo ""

echo "[2/6] Starting FinanceService (port 5300)..."
(cd "$SCRIPT_DIR/src/SeqDemo.FinanceService" && dotnet run) &
PIDS+=($!)
sleep 3
echo ""

echo "[3/6] Starting PlayerGameService (port 5200)..."
(cd "$SCRIPT_DIR/src/SeqDemo.PlayerGameService" && dotnet run) &
PIDS+=($!)
sleep 3
echo ""

echo "[4/6] Starting GatewayService (port 5100)..."
(cd "$SCRIPT_DIR/src/SeqDemo.GatewayService" && dotnet run) &
PIDS+=($!)
sleep 2
echo ""

echo "[5/6] Starting NotificationService (Kafka consumer)..."
(cd "$SCRIPT_DIR/src/SeqDemo.NotificationService" && dotnet run) &
PIDS+=($!)
echo ""

echo "[6/6] All services started!"
echo ""
echo "  FinanceService:       port 5300 (gRPC server)"
echo "  PlayerGameService:    port 5200 (gRPC server)"
echo "  GatewayService:       port 5100 (gRPC client / workflow orchestrator)"
echo "  NotificationService:  Kafka consumer"
echo ""
echo "  Seq:      http://localhost:5341"
echo "  Grafana:  http://localhost:3000"
echo "  Kafka UI: http://localhost:8080"
echo ""

if command -v xdg-open > /dev/null; then
    xdg-open http://localhost:5341
elif command -v open > /dev/null; then
    open http://localhost:5341
fi

echo "Press Ctrl+C to stop all services..."
wait
