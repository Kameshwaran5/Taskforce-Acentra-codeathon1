#!/usr/bin/env bash
set -e

BASE_URL="http://localhost:5000"
echo "================================================================================"
echo "    RESERVEPULSE CONCURRENCY VERIFICATION TEST (POSTGRESQL SERIALIZABLE)       "
echo "================================================================================"
echo "Base URL: $BASE_URL"
echo ""

# 1. Health check & Fetch Active Resources
echo "[STEP 1] Checking API & Fetching Active Resources..."
RESOURCES_RESP=$(curl -s -w "\n%{http_code}" "$BASE_URL/api/resources")
HTTP_CODE=$(echo "$RESOURCES_RESP" | tail -n 1)
BODY=$(echo "$RESOURCES_RESP" | sed '$d')

if [ "$HTTP_CODE" != "200" ]; then
  echo "[-] ERROR: /api/resources returned HTTP $HTTP_CODE"
  echo "$BODY"
  exit 1
fi

RESOURCE_ID=$(echo "$BODY" | grep -o '"id":[0-9]*' | head -n 1 | cut -d':' -f2)
RESOURCE_NAME=$(echo "$BODY" | grep -o '"name":"[^"]*' | head -n 1 | cut -d'"' -f4)
echo "[+] Connected! Target Resource: ID $RESOURCE_ID ('$RESOURCE_NAME')"
echo ""

# 2. Setup overlapping test window for simultaneous booking
# Generate a unique timestamp offset so each run tests a fresh time window
EPOCH=$(date +%s)
START_UTC="2026-10-15T14:00:00Z"
END_UTC="2026-10-15T15:30:00Z"

# Randomize date/time for multi-run safety
RAND_HOUR=$(( (EPOCH % 5) + 9 ))
RAND_DAY=$(( (EPOCH % 20) + 1 ))
START_UTC=$(printf "2026-11-%02dT%02d:00:00Z" $RAND_DAY $RAND_HOUR)
END_UTC=$(printf "2026-11-%02dT%02d:30:00Z" $RAND_DAY $((RAND_HOUR + 1)))

echo "================================================================================"
echo "[STEP 2] SIMULTANEOUS DOUBLE-BOOKING RACE TEST"
echo "Target Resource ID: $RESOURCE_ID"
echo "Target Window     : $START_UTC to $END_UTC"
echo "Simulating two concurrent users (Alice Cooper vs Bob Dylan) hitting the endpoint"
echo "at the exact same millisecond with identical resource and overlapping time range..."
echo "================================================================================"

PAYLOAD_A=$(cat <<EOF
{
  "resourceId": $RESOURCE_ID,
  "userName": "Alice Cooper",
  "userEmail": "alice@race-demo.com",
  "startUtc": "$START_UTC",
  "endUtc": "$END_UTC"
}
EOF
)

PAYLOAD_B=$(cat <<EOF
{
  "resourceId": $RESOURCE_ID,
  "userName": "Bob Dylan",
  "userEmail": "bob@race-demo.com",
  "startUtc": "$START_UTC",
  "endUtc": "$END_UTC"
}
EOF
)

RESP_A_FILE=$(mktemp)
RESP_B_FILE=$(mktemp)

# Fire both requests simultaneously using background processes and wait
curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/bookings" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD_A" > "$RESP_A_FILE" &
PID_A=$!

curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/bookings" \
  -H "Content-Type: application/json" \
  -d "$PAYLOAD_B" > "$RESP_B_FILE" &
PID_B=$!

wait $PID_A $PID_B

CODE_A=$(tail -n 1 "$RESP_A_FILE")
BODY_A=$(sed '$d' "$RESP_A_FILE")
CODE_B=$(tail -n 1 "$RESP_B_FILE")
BODY_B=$(sed '$d' "$RESP_B_FILE")

rm -f "$RESP_A_FILE" "$RESP_B_FILE"

echo ""
echo "--- [CLIENT 1: Alice Cooper Response] ---"
echo "HTTP Status : $CODE_A"
echo "Payload     : $BODY_A"
echo ""
echo "--- [CLIENT 2: Bob Dylan Response] ---"
echo "HTTP Status : $CODE_B"
echo "Payload     : $BODY_B"
echo "-----------------------------------------"

# Evaluate race condition outcome
BOOKING_ID=""
VERSION_TOKEN=""
OWNER_TOKEN=""

if [ "$CODE_A" == "200" ] && [ "$CODE_B" == "409" ]; then
  echo "[PASS] RESULT: Alice succeeded (200 OK), Bob conflicted (409 Conflict)!"
  BOOKING_ID=$(echo "$BODY_A" | grep -o '"id":[0-9]*' | head -n 1 | cut -d':' -f2)
  VERSION_TOKEN=$(echo "$BODY_A" | grep -o '"version":"[^"]*' | head -n 1 | cut -d'"' -f4)
  OWNER_TOKEN=$(echo "$BODY_A" | grep -o '"ownerToken":"[^"]*' | head -n 1 | cut -d'"' -f4)
elif [ "$CODE_A" == "409" ] && [ "$CODE_B" == "200" ]; then
  echo "[PASS] RESULT: Bob succeeded (200 OK), Alice conflicted (409 Conflict)!"
  BOOKING_ID=$(echo "$BODY_B" | grep -o '"id":[0-9]*' | head -n 1 | cut -d':' -f2)
  VERSION_TOKEN=$(echo "$BODY_B" | grep -o '"version":"[^"]*' | head -n 1 | cut -d'"' -f4)
  OWNER_TOKEN=$(echo "$BODY_B" | grep -o '"ownerToken":"[^"]*' | head -n 1 | cut -d'"' -f4)
else
  echo "[FAIL] Unexpected status code combination: Request A = $CODE_A, Request B = $CODE_B"
  exit 1
fi

echo "[+] Successfully prevented double-booking! Created Booking ID: $BOOKING_ID"
echo "    Concurrency Token Version: $VERSION_TOKEN"
echo "    Private OwnerToken       : $OWNER_TOKEN"
echo ""

# 3. Test Concurrent Cancellation with Same Version Token & Valid OwnerToken
echo "================================================================================"
echo "[STEP 3] SIMULTANEOUS CANCELLATION RACE TEST (CONCURRENCY TOKEN + ACCESS TOKEN)"
echo "Target Booking ID : $BOOKING_ID"
echo "Version Token     : $VERSION_TOKEN"
echo "Owner Token       : $OWNER_TOKEN"
echo "Simulating two concurrent cancellations carrying valid OwnerToken and identical Version..."
echo "================================================================================"

CANCEL_PAYLOAD=$(cat <<EOF
{
  "version": "$VERSION_TOKEN",
  "ownerToken": "$OWNER_TOKEN"
}
EOF
)

CANCEL_A_FILE=$(mktemp)
CANCEL_B_FILE=$(mktemp)

curl -s -w "\n%{http_code}" -X DELETE "$BASE_URL/api/bookings/$BOOKING_ID" \
  -H "Content-Type: application/json" \
  -d "$CANCEL_PAYLOAD" > "$CANCEL_A_FILE" &
PID_C1=$!

curl -s -w "\n%{http_code}" -X DELETE "$BASE_URL/api/bookings/$BOOKING_ID" \
  -H "Content-Type: application/json" \
  -d "$CANCEL_PAYLOAD" > "$CANCEL_B_FILE" &
PID_C2=$!

wait $PID_C1 $PID_C2

CODE_C1=$(tail -n 1 "$CANCEL_A_FILE")
BODY_C1=$(sed '$d' "$CANCEL_A_FILE")
CODE_C2=$(tail -n 1 "$CANCEL_B_FILE")
BODY_C2=$(sed '$d' "$CANCEL_B_FILE")

rm -f "$CANCEL_A_FILE" "$CANCEL_B_FILE"

echo ""
echo "--- [Cancel Request 1 Response] ---"
echo "HTTP Status : $CODE_C1"
echo "Payload     : $BODY_C1"
echo ""
echo "--- [Cancel Request 2 Response] ---"
echo "HTTP Status : $CODE_C2"
echo "Payload     : $BODY_C2"
echo "-----------------------------------"

if ([ "$CODE_C1" == "200" ] && [ "$CODE_C2" == "409" ]) || ([ "$CODE_C1" == "409" ] && [ "$CODE_C2" == "200" ]); then
  echo "[PASS] RESULT: Exactly one cancellation succeeded (200 OK) and the concurrent one was rejected with (409 Conflict)!"
else
  echo "[FAIL] Unexpected cancellation status codes: Cancel 1 = $CODE_C1, Cancel 2 = $CODE_C2"
  exit 1
fi

echo ""

# 4. Access Control Verification Test (HTTP 403 Forbidden)
echo "================================================================================"
echo "[STEP 4] ACCESS CONTROL VERIFICATION TEST (HTTP 403 FORBIDDEN)"
echo "Testing cancellation security: verify that attempting to cancel without the"
echo "private OwnerToken is strictly forbidden (HTTP 403)."
echo "================================================================================"

# Create a fresh booking for access control testing
TEST_START=$(printf "2026-12-%02dT10:00:00Z" $RAND_DAY)
TEST_END=$(printf "2026-12-%02dT11:00:00Z" $RAND_DAY)

NEW_BOOK_RESP=$(curl -s -w "\n%{http_code}" -X POST "$BASE_URL/api/bookings" \
  -H "Content-Type: application/json" \
  -d "{
    \"resourceId\": $RESOURCE_ID,
    \"userName\": \"Security Test Owner\",
    \"userEmail\": \"owner@security-test.com\",
    \"startUtc\": \"$TEST_START\",
    \"endUtc\": \"$TEST_END\"
  }")

AUTH_BOOK_CODE=$(echo "$NEW_BOOK_RESP" | tail -n 1)
AUTH_BOOK_BODY=$(echo "$NEW_BOOK_RESP" | sed '$d')

if [ "$AUTH_BOOK_CODE" != "200" ]; then
  echo "[-] Failed to create booking for access control test: $AUTH_BOOK_BODY"
  exit 1
fi

AUTH_ID=$(echo "$AUTH_BOOK_BODY" | grep -o '"id":[0-9]*' | head -n 1 | cut -d':' -f2)
AUTH_VER=$(echo "$AUTH_BOOK_BODY" | grep -o '"version":"[^"]*' | head -n 1 | cut -d'"' -f4)
REAL_TOKEN=$(echo "$AUTH_BOOK_BODY" | grep -o '"ownerToken":"[^"]*' | head -n 1 | cut -d'"' -f4)

echo "[+] Created test booking ID $AUTH_ID. Real OwnerToken: $REAL_TOKEN"

# 4a. Attempt cancel with WRONG OwnerToken
FAKE_TOKEN="00000000-0000-0000-0000-000000000000"
UNAUTH_RESP=$(curl -s -w "\n%{http_code}" -X DELETE "$BASE_URL/api/bookings/$AUTH_ID" \
  -H "Content-Type: application/json" \
  -d "{\"version\": \"$AUTH_VER\", \"ownerToken\": \"$FAKE_TOKEN\"}")

UNAUTH_CODE=$(echo "$UNAUTH_RESP" | tail -n 1)
UNAUTH_BODY=$(echo "$UNAUTH_RESP" | sed '$d')

echo "--- [Unauthorized Cancel Attempt (Invalid Token)] ---"
echo "HTTP Status : $UNAUTH_CODE"
echo "Payload     : $UNAUTH_BODY"

if [ "$UNAUTH_CODE" == "403" ]; then
  echo "[PASS] Correctly rejected unauthorized cancellation with HTTP 403 Forbidden!"
else
  echo "[FAIL] Expected HTTP 403 Forbidden, but received $UNAUTH_CODE"
  exit 1
fi

# 4b. Cancel with LEGITIMATE OwnerToken
LEGIT_RESP=$(curl -s -w "\n%{http_code}" -X DELETE "$BASE_URL/api/bookings/$AUTH_ID" \
  -H "Content-Type: application/json" \
  -d "{\"version\": \"$AUTH_VER\", \"ownerToken\": \"$REAL_TOKEN\"}")

LEGIT_CODE=$(echo "$LEGIT_RESP" | tail -n 1)
LEGIT_BODY=$(echo "$LEGIT_RESP" | sed '$d')

echo ""
echo "--- [Authorized Cancel Attempt (Legitimate Token)] ---"
echo "HTTP Status : $LEGIT_CODE"
echo "Payload     : $LEGIT_BODY"

if [ "$LEGIT_CODE" == "200" ]; then
  echo "[PASS] Legitimate owner cancellation succeeded with HTTP 200 OK!"
else
  echo "[FAIL] Expected HTTP 200 OK for owner, but received $LEGIT_CODE"
  exit 1
fi

echo ""
echo "================================================================================"
echo "    ALL TESTS PASSED! CONCURRENCY (409) & ACCESS CONTROL (403) VERIFIED.      "
echo "================================================================================"
