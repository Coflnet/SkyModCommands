# Lowball Offers Feature

This feature allows users to create and browse lowball offers for Hypixel Skyblock items through both in-game commands and REST API endpoints.

## Overview

Lowball offers are stored in Cassandra with a 7-day TTL and published to Kafka topic `sky-lowball-offers`. The system provides:
- Dual indexing (by user and by item tag)
- Pagination using "before DateTime" pattern
- Filter matching similar to flip filters
- In-game command integration for easy offer creation

## Components

### 1. Data Models (`Models/LowballOffer.cs`)

Two Cassandra tables:

#### `lowball_offers` (by user)
- **Partition Key**: `user_id`
- **Clustering Keys**: `created_at` (descending), `offer_id`
- **Fields**: ItemTag, ItemName, NbtData, Filters, AskingPrice, Lore, ItemCount
- **TTL**: 7 days

#### `lowball_offers_by_item` (by item tag)
- **Partition Key**: `item_tag`
- **Clustering Keys**: `created_at` (descending), `offer_id`
- **Fields**: UserId, ItemName, NbtData, Filters, AskingPrice, Lore, ItemCount
- **TTL**: 7 days

### 2. Service Layer (`Services/LowballOfferService.cs`)

**Methods**:
- `CreateOffer(userId, item, askingPrice, filters)` - Create new offer, store in both tables, publish to Kafka
- `GetOffersByUser(userId, before?, limit)` - Paginated user offers
- `GetOffersByItem(itemTag, filters?, before?, limit)` - Paginated item offers with filter matching
- `DeleteOffer(userId, offerId)` - Delete offer from both tables

**Kafka Integration**:
- Topic: `sky-lowball-offers`
- Producer: `KafkaCreator.BuildProducer<string, LowballOffer>()`
- Key: offer_id (Guid string)
- Value: LowballOffer object

### 3. REST API (`Controllers/LowballOfferController.cs`)

#### Endpoints

**GET /api/lowball/user/{userId}**
- Get offers by user
- Query params:
  - `before` (DateTimeOffset, optional) - Pagination cursor
  - `limit` (int, default 20, max 100)
- Returns: `List<LowballOffer>`

**GET /api/lowball/item/{itemTag}**
- Get offers by item tag with filter matching
- Query params:
  - `before` (DateTimeOffset, optional) - Pagination cursor
  - `limit` (int, default 20, max 100)
  - `filter` (Dictionary<string, string>, optional) - Filter criteria
- Returns: `List<LowballOfferByItem>`

**DELETE /api/lowball/user/{userId}/offer/{offerId}**
- Delete a specific offer
- Returns: 200 OK or 404 Not Found

### 4. In-Game Command (`Commands/Minecraft/HotkeyCommand.cs`)

**Usage**: `/cl offer_lowball|<nbt>|<price>`

When a player holds an item and executes the command with their desired asking price:
1. NBT data is parsed and item details extracted
2. Item filters are automatically requested from the pricing API
3. Offer is created and stored
4. Confirmation message is shown to the player

**Example**:
```
/cl offer_lowball|{...nbt...}|5000000
```

Response:
```
Lowball offer created!
Item: Hyperion
Asking price: 500M
Offer ID: 3fa85f64-5717-4562-b3fc-2c963f66afa6
Your offer will be visible for 7 days
```

## Pagination

The system uses **"before DateTime"** pattern for pagination:

1. First request: `/api/lowball/item/HYPERION?limit=20`
2. Get the `created_at` of the last item in results
3. Next request: `/api/lowball/item/HYPERION?before=2024-01-15T10:30:00Z&limit=20`

This works because `created_at` is a clustering key with descending order.

## Filter Matching

Filters work similar to flip filters:

### Creating an offer with filters
When a user creates an offer via HotkeyCommand, filters are automatically extracted from the item:
- Reforge
- Tier (rarity)
- Enchantments
- Other item-specific attributes

### Querying with filters
```
GET /api/lowball/item/HYPERION?filter[reforge]=withered&filter[tier]=LEGENDARY
```

The service will only return offers where ALL specified filters match the offer's stored filters.

## Architecture Decisions

1. **Dual Indexing**: Separate tables for user-centric and item-centric queries without expensive secondary indexes
2. **TTL**: 7-day automatic expiration reduces storage and keeps offers fresh
3. **Kafka Publishing**: Enables downstream processing, analytics, and notifications
4. **Clustering Key Pattern**: `created_at DESC` + `offer_id` ensures chronological ordering and uniqueness
5. **Filter Overlap Fetching**: Fetch 3x limit then filter to handle sparse filter matches efficiently

## Configuration

Ensure these are configured in `appsettings.json`:

```json
{
  "TOPICS": {
    "sky-lowball-offers": "sky-lowball-offers"
  },
  "KAFKA": {
    "BROKERS": "kafka-broker:9092"
  }
}
```

## Future Enhancements

- Push notifications when offers match buyer criteria
- Reputation/rating system for sellers
- Automatic price suggestions based on market data
- Bulk offer creation
- Offer editing (currently requires delete + recreate)
