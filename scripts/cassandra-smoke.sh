#!/usr/bin/env bash
set -Eeuo pipefail

readonly CASSANDRA_IMAGE="docker.io/library/cassandra:4.1.7@sha256:17850cb6e5f96a58bce0d9c7e3fbe470b6b73a554e1abe2b8d300552baa6cf43"
readonly READINESS_TIMEOUT_SECONDS=180
readonly TOTAL_TIMEOUT_SECONDS=420
readonly TEST_NAME="Coflnet.Sky.ModCommands.Services.CassandraSmokeTests.BlockedReasonsServerRoundTrip"

print_plan() {
    printf '%s\n' \
        "PINNED_IMAGE=${CASSANDRA_IMAGE}" \
        "READINESS_TIMEOUT_SECONDS=${READINESS_TIMEOUT_SECONDS}" \
        "TOTAL_TIMEOUT_SECONDS=${TOTAL_TIMEOUT_SECONDS}" \
        "RESOURCES=container,network,volume" \
        "CLEANUP=always" \
        "TIMEOUT_CLEANUP=surviving-parent" \
        "DIAGNOSTICS=docker logs --tail 40" \
        "STARTUP_OUTPUT=bounded" \
        "TEST_OUTPUT=bounded" \
        "TEST=dotnet test SkyModCommands.csproj --filter FullyQualifiedName=${TEST_NAME}"
}

if [[ "${1:-}" == "--plan" ]]; then
    print_plan
    exit 0
fi

readonly SCRIPT_DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_DIRECTORY="$(cd "${SCRIPT_DIRECTORY}/.." && pwd)"
readonly SCRIPT_PATH="${SCRIPT_DIRECTORY}/$(basename "${BASH_SOURCE[0]}")"
readonly RESOURCE_SUFFIX="${CASSANDRA_SMOKE_RESOURCE_SUFFIX:-$$-${RANDOM}}"
readonly CONTAINER_NAME="sky-mod-commands-cassandra-${RESOURCE_SUFFIX}"
readonly NETWORK_NAME="sky-mod-commands-cassandra-${RESOURCE_SUFFIX}"
readonly VOLUME_NAME="sky-mod-commands-cassandra-${RESOURCE_SUFFIX}"
startup_output_file="${CASSANDRA_SMOKE_STARTUP_OUTPUT_FILE:-}"
test_output_file="${CASSANDRA_SMOKE_TEST_OUTPUT_FILE:-}"

bounded_diagnostics() {
    if timeout 5s docker inspect "${CONTAINER_NAME}" >/dev/null 2>&1; then
        printf '%s\n' "Cassandra smoke failed; final container status and log tail:"
        timeout 5s docker ps --all --filter "name=^/${CONTAINER_NAME}$" --format '{{.Status}}' || true
        timeout 5s docker logs --tail 40 "${CONTAINER_NAME}" 2>&1 || true
    fi
}

cleanup() {
    local exit_code=$?
    trap - EXIT INT TERM
    if [[ "${exit_code}" != "0" ]]; then
        bounded_diagnostics
    fi
    timeout 5s docker rm --force "${CONTAINER_NAME}" >/dev/null 2>&1 || true
    timeout 5s docker network rm "${NETWORK_NAME}" >/dev/null 2>&1 || true
    timeout 5s docker volume rm --force "${VOLUME_NAME}" >/dev/null 2>&1 || true
    if [[ -n "${startup_output_file}" && -f "${startup_output_file}" ]]; then
        unlink "${startup_output_file}"
    fi
    if [[ -n "${test_output_file}" && -f "${test_output_file}" ]]; then
        unlink "${test_output_file}"
    fi
    exit "${exit_code}"
}

if [[ "${CASSANDRA_SMOKE_UNDER_TIMEOUT:-0}" != "1" ]]; then
    startup_output_file="$(mktemp "${TMPDIR:-/tmp}/cassandra-smoke-startup.XXXXXX.log")"
    test_output_file="$(mktemp "${TMPDIR:-/tmp}/cassandra-smoke-test.XXXXXX.log")"
    trap cleanup EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    worker_status=0
    timeout --signal=TERM --kill-after=45s "${TOTAL_TIMEOUT_SECONDS}s" \
        env CASSANDRA_SMOKE_UNDER_TIMEOUT=1 \
        CASSANDRA_SMOKE_RESOURCE_SUFFIX="${RESOURCE_SUFFIX}" \
        CASSANDRA_SMOKE_STARTUP_OUTPUT_FILE="${startup_output_file}" \
        CASSANDRA_SMOKE_TEST_OUTPUT_FILE="${test_output_file}" \
        "${SCRIPT_PATH}" || worker_status=$?
    exit "${worker_status}"
fi

docker network create "${NETWORK_NAME}" >/dev/null
docker volume create "${VOLUME_NAME}" >/dev/null
if ! docker run --detach \
    --name "${CONTAINER_NAME}" \
    --network "${NETWORK_NAME}" \
    --mount "type=volume,source=${VOLUME_NAME},target=/var/lib/cassandra" \
    --publish 127.0.0.1::9042 \
    --env CASSANDRA_CLUSTER_NAME=sky-mod-commands-smoke \
    --env MAX_HEAP_SIZE=512M \
    --env HEAP_NEWSIZE=100M \
    "${CASSANDRA_IMAGE}" >"${startup_output_file}" 2>&1; then
    printf '%s\n' "Could not start the Cassandra container; final Docker output:" >&2
    tail -40 "${startup_output_file}" >&2
    exit 1
fi

readiness_deadline=$((SECONDS + READINESS_TIMEOUT_SECONDS))
until timeout 10s docker exec "${CONTAINER_NAME}" cqlsh --execute \
    "SELECT release_version FROM system.local" >/dev/null 2>&1; do
    if (( SECONDS >= readiness_deadline )); then
        printf '%s\n' "Cassandra did not become ready within ${READINESS_TIMEOUT_SECONDS}s." >&2
        exit 1
    fi
    sleep 2
done

published_address="$(docker port "${CONTAINER_NAME}" 9042/tcp | head -n 1)"
published_port="${published_address##*:}"
if [[ ! "${published_port}" =~ ^[0-9]+$ ]]; then
    printf '%s\n' "Could not determine Cassandra's published port." >&2
    exit 1
fi

cd "${PROJECT_DIRECTORY}"
test_status=0
CASSANDRA_SMOKE_HOST=127.0.0.1 CASSANDRA_SMOKE_PORT="${published_port}" \
    dotnet test SkyModCommands.csproj --filter "FullyQualifiedName=${TEST_NAME}" --logger "console;verbosity=detailed" >"${test_output_file}" 2>&1 || test_status=$?
if [[ "${test_status}" != "0" ]]; then
    printf '%s\n' "Cassandra smoke test failed; final test output:"
    tail -40 "${test_output_file}"
    exit "${test_status}"
fi
grep -E 'Server message for existing column' "${test_output_file}" | head -n 1 || true
grep -E 'Passed!.*Total:' "${test_output_file}" | tail -n 1 || true
printf '%s\n' "Blocked reasons Cassandra smoke passed."
