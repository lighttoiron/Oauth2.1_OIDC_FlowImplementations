#!/bin/bash
# Run from oauth-lab root ./run-all.sh
# May need to make this script executable using chmod +x run-all.sh

# Wait for the server at the given url to respond
wait_for_ready() {
    local name=$1
    local url=$2
    local max_attempts=30
    local attempt=0

    echo "Waiting for $name to be ready..."
    while [ $attempt -lt $max_attempts ]; do
        # -s silent, -o /dev/null discard body, -k ignore cert errors,
        # --max-time 2 don't wait more than 2 seconds per attempt
        if curl -s -o /dev/null -k --max-time 2 "$url" 2>/dev/null; then
            echo "$name is ready."
            return 0
        fi
        attempt=$((attempt + 1))
        sleep 1
    done

    echo "ERROR: $name failed to start after $max_attempts seconds."
    return 1
}

# Cleans up any existing processes we've started
cleanup () {
    echo ""
    echo "Shutting down all projects..."
    kill $AUTH_PID $API_PID $CLIENT_PID 2>/dev/null
    wait $AUTH_PID $API_PID $CLIENT_PID 2>/dev/null
    echo "All projects stopped."
    echo ""
}
# Run the cleanup function if this process receives an EXIT, INT, or TERM signal
trap cleanup EXIT INT TERM

echo "Starting OAuth Lab..."
echo ""

#AuthServer needs to start first so that the API server can register with it through the discovery endpoint
cd AuthServer
# Run the https profile of the AuthServer project in the background
dotnet run --launch-profile https &
AUTH_PID=$!
cd ..

# Wait for the AuthServer to start responding so we know we are ready to run the API and Client servers
wait_for_ready "AuthServer" "https://localhost:7010/.well-known/openid-configuration"
# If the wait_for_ready function returned anything other than success, abort.
if [ $? -ne 0 ]; then
    echo "AuthServer failed to start. Aborting."
    exit 1
fi

# Launch the ResourceAPI Server next
cd ResourceApi
dotnet run --launnch-profile https &
API_PID=$!
cd ..

# Then launch the Client Server.  No need to wait for ready, does not rely on other servers for startup.
cd Client
dotnet run --launch-profile https &
CLIENT_PID=$!
cd ..

# Wait for the client server to respond
wait_for_ready "Client" "https://localhost:7000"

echo ""
echo "All projects ready."
echo ""
echo " Client:       https://localhost:7000"
echo " Auth Server:  https://localhost:7010"
echo " Resource API: https://localhost:7020"
echo ""

# Wait for the three servers to close before exiting this process
wait $AUTH_PID $API_PID $CLIENT_PID