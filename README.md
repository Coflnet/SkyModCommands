# SkyModCommands


## Commands
### Models
#### ChatPart
Used to transfer a chat message with click action and hover text.
```json
{
    "text":"Hello World",
    "onClick":"suggest_command:/auction",
    "hover":"show_text:\"Click to suggest /auction\""
}
```
#### Auction
Representation of an auction from the api
```json
{
    {"enchantments":[],"uuid":"be02f8df060644b8b9c7a69ad027a587","count":1,"startingBid":5000000,"tag":"PET_BAT","itemName":"[Lvl 63] Bat","start":"2023-05-29T02:57:37","end":"2023-05-29T08:52:37","auctioneerId":"84ba980026f640fba18ca14ad5f540d8","profileId":"888dee1e5eb04242acfcfba060f2ff85","coop":null,"coopMembers":null,"highestBidAmount":5000000,"bids":[{"bidder":"7f9fbe96ffc4468f8e896dc35a5b2f4a","profileId":"unknown","amount":5000000,"timestamp":"2023-05-29T08:52:37"}],"anvilUses":0,"nbtData":{},"itemCreatedAt":"2023-05-28T22:55:00","reforge":"None","category":"MISC","tier":"MYTHIC","bin":true,"flatNbt":{"type":"BAT","active":"False"}}
}
```
Used in multiple commands.
### Server to Client
#### flip
Data about a flipable auction.
```json
{
    "message":[{ChatPart}],
    "id":"auctionuuid",
    "worth":12345678, // higher the "better" a flip is
    "sound":{"name":"note.pling","pitch":1},
    "auction":{Auction},
    "render":"leather_leggings" // the item name to render, if length 64 not an item id but a skull texture id 
}
```

#### chatMessage
A message that should be displayed in the chat.
```json
[{ChatPart}]
```
#### privacySettings
Transmits users the privacy settings on login or when they changed so some data is started to/no longer uploaded.
```json
{
    "chatRegex":".*",
    "collectChat":true,
    "collectInventory":true,
    "collectTab":true,
    "collectScoreboard":true,
    "allowProxy":true,
    "collectLobbyChanges":true,
    "collectEntities":true,
    "collectLocation":true,
    "extendDescriptions":true,
    "commandPrefixes":["/cofl","/colf","/fc"],
    "autoStart":true,
}
```
#### error
An error message something went severly wrong.
```json
"message"
```

#### writeToChat
First version of chatMessage only supporting one element.
```json
{ChatPart}
```
#### execute
Executes a command on the client. Exists for extensibility.
Currently used to 
* open players ahs directly
* trigger commands in the client
* test ping (executing a /cofl command and timing the response)

poses a potential security risk if the server is not trusted.
```json
"command"
```

#### ping
Used to keep the connection alive. Cloudflare closes inactive connections after one minute.
Can be ignored by the client.
```json
0
```

#### countdown
Triggers a countdown to be displayed.
```json
{
    "seconds":12.3,
    "widthPercent":10,
    "heightPercent":10,
    "scale":2,
    "prefix":"§c",
    "maxPrecision":3
}
```

#### getMods
Requests the mods the client has installed. Used for compatibility checks.
```json
0
```

#### playSound
Plays a sound on the client.
```json
{
    "name":"note.pling",
    "pitch":1
}
```

#### log
Logs a message on the client. Used for debugging.
```json
"message"
```

#### settings
Response to "get","json" command returns a json view of all the available settings and their current values.
                    key = o.Key,
                    name = o.Value.RealName,
                    value = await updater.GetCurrentValue(socket, o.Key),
                    info = o.Value.Info,
                    type = o.Value.Type,
                    category = o.Key.Substring(0, 3) switch
                    {
                        "mod" => "mod",
                        "sho" => "visibility",
                        "pri" => "privacy",
                        _ => "general"
                    }
```json
[
    {
        "key":"minProfit",
        "name":"Minimum Profit",
        "value":123456,
        "info":"The minimum amount of profit a flip should make to be displayed.",
        "type":"bool|int|double|string",
        "category":"mod|visibility|privacy|general"
    }
]
```

#### tier
Response to "get","tier" command returns the users current plan tier
```json
{
    "tier":"PREMIUM",
    "expires":"2023-05-29T08:52:37"
}
```