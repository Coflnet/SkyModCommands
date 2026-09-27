# Proxy System Implementation

## Overview
This implementation adds a comprehensive HTTP request proxying system that allows the server to request web content through mod users. Users can opt in/out of proxying, and requests can be filtered by user locale.

## Components Created

### 1. Database Model (`Services/ProxyResponseTable.cs`)
- Cassandra table model for storing proxy responses
- Fields: Id, RequestUrl, ResponseBody, StatusCode, Headers, UserId, Locale, CreatedAt
- TTL: 1 hour (automatic expiration)
- Uses CQL LINQ pattern like CounterService

### 2. Proxy Service (`Services/ProxyService.cs`)
- **Socket Management**: Maintains queues of available proxy users by locale
- **Request Routing**: Routes proxy requests to users based on locale preference with fallback
- **Response Storage**: Stores proxied responses using Cassandra LINQ
- **Response Retrieval**: Fetches stored responses by ID
- **Statistics**: Provides counts of available sockets by locale

Key Methods:
- `RegisterSocket(socket)`: Registers a user for proxy requests
- `UnregisterSocket(socket)`: Removes a user from proxy pool
- `RequestProxy(url, uploadTo, locale, regex)`: Sends proxy request to a user
- `StoreProxyResponse(...)`: Saves proxied content to database
- `GetProxyResponse(id)`: Retrieves stored proxy response

### 3. Proxy Command (`Commands/Minecraft/ProxyCommand.cs`)
User-facing command to manage proxy opt-in status:
- `/cofl proxy` - Show current status
- `/cofl proxy on` - Enable proxying
- `/cofl proxy off` - Disable proxying

The command:
- Updates `AccountInfo.ProxyOptIn` in database
- Updates `SessionInfo.ProxyOptIn` in memory
- Registers/unregisters socket with ProxyService

### 4. API Controller (`Controllers/ProxyController.cs`)
Three HTTP endpoints:

#### POST `/api/proxy/request`
Request a proxy for a URL
```json
{
  "url": "https://example.com",
  "uploadTo": "https://yourserver.com/api/proxy/response",
  "locale": "en_US",  // optional
  "regex": "pattern"   // optional
}
```

Response:
```json
{
  "id": "request-uuid",
  "message": "Proxy request sent"
}
```

#### POST `/api/proxy/response`
Submit a proxied response (called by mod)
```json
{
  "id": "request-uuid",
  "requestUrl": "https://example.com",
  "responseBody": "HTML content...",
  "statusCode": 200,
  "headers": "{...}",
  "userId": "user-id",
  "locale": "en_US"
}
```

#### GET `/api/proxy/{id}`
Retrieve a stored proxy response
Returns the full ProxyResponseTable object

#### GET `/api/proxy/stats`
Get statistics about available proxy users
```json
{
  "totalAvailable": 42,
  "byLocale": {
    "en_US": 20,
    "de_DE": 15,
    "fr_FR": 7
  }
}
```

### 5. Data Model Updates

#### `AccountInfo` (`SkyBackendForFrontend/Models/Account/AccountInfo.cs`)
Added fields:
- `ProxyOptIn` (bool): Persistent opt-in status

#### `SessionInfo` (`Commands/SessionInfo.cs`)
Added fields:
- `ProxyOptIn` (bool): Current session proxy status
- `Locale` (string?): User's locale for location-based requests

### 6. Integration Points

#### ModSessionLifesycle (`Commands/ModSessionLifesycle.cs`)
**In `UpdateAccountInfo` method**:
- Syncs `ProxyOptIn` from AccountInfo to SessionInfo
- Syncs `Locale` from AccountInfo to SessionInfo
- Registers/unregisters socket with ProxyService based on opt-in status

**In `Dispose` method**:
- Unregisters socket from ProxyService on disconnect

#### Startup (`Startup.cs`)
Registered ProxyService as singleton:
```csharp
services.AddSingleton<ProxyService>();
```

## Usage Flow

### User Opt-In Flow
1. User runs `/cofl proxy on`
2. Command updates `AccountInfo.ProxyOptIn = true` in database
3. Command updates `SessionInfo.ProxyOptIn = true` in memory
4. Command calls `ProxyService.RegisterSocket(socket)`
5. Socket is added to locale-specific queue

### Proxy Request Flow
1. External service calls `POST /api/proxy/request` with URL and optional locale
2. ProxyService finds an available socket (prefer matching locale)
3. ProxyService sends `proxy` message to mod client with request details
4. Mod client fetches the URL
5. Mod client POSTs response to configured `uploadTo` endpoint
6. Response is stored in Cassandra with 1-hour TTL
7. External service retrieves response via `GET /api/proxy/{id}`

### Socket Management
- Sockets are automatically registered when users opt in
- Sockets are re-queued after handling a request (round-robin)
- Sockets are unregistered on disconnect or opt-out
- Locale-based routing with fallback to any available socket

## Database Schema

### Cassandra Table: `proxy_responses`
```cql
CREATE TABLE proxy_responses (
    id text PRIMARY KEY,
    request_url text,
    response_body text,
    status_code int,
    headers text,
    user_id text,
    locale text,
    created_at timestamp
) WITH default_time_to_live = 3600;
```

## Security Considerations

- Users must explicitly opt in
- Proxy requests are only sent to opted-in users
- 1-hour TTL ensures data doesn't persist indefinitely
- User ID is tracked for accountability
- Users can opt out at any time

## Testing

To test the implementation:

1. **User opt-in**:
   ```
   /cofl proxy on
   ```

2. **Request a proxy** (from external service):
   ```bash
   curl -X POST http://localhost:5000/api/proxy/request \
     -H "Content-Type: application/json" \
     -d '{"url": "https://example.com", "uploadTo": "http://yourserver.com/api/proxy/response"}'
   ```

3. **Check stats**:
   ```bash
   curl http://localhost:5000/api/proxy/stats
   ```

4. **Retrieve response**:
   ```bash
   curl http://localhost:5000/api/proxy/{id}
   ```

## Notes

- The implementation uses CQL LINQ throughout, following the CounterService pattern
- Automatic TTL (1 hour) handles cleanup without manual intervention
- Socket queues are managed in-memory for performance
- Locale-based routing allows for region-specific content fetching
- The system is resilient to socket disconnections and user opt-outs
