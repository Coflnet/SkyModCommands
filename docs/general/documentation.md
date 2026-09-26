# Backend command flow

## Runtime ownership

`MinecraftSocket.Commands` registers `TradeGuiCommand` alongside the existing
commands. Its public help description and `CompletionArguments` are serialized
by `HelpCommand.BuildCommandList` into the existing `commandUpdate` dictionary.
The command list includes `tradegui`, `tradegui on`, and `tradegui off`. Argument
metadata is generic and does not require a Fabric literal command registration.

CoflSkyCore stores the dictionary in `knownCommands`; Fabric supplies suggestions
from that map. Fabric forwards execution through Core's ordinary backend command
path. `TradeGuiCommand` validates the JSON string argument and sends `tradeGui`
with an `enabled` Boolean. An empty argument sends `enabled: null`, asking the
client to display its persisted state. Invalid arguments return usage without a
state change. Argument comparison ignores case and surrounding whitespace.

The standard `Response` envelope is unchanged. `data` contains a JSON encoded
string, not an embedded object. For example:

```json
{"type":"tradeGui","data":"{\"enabled\":false}"}
```

The matching Core library dispatches a validated `OnTradeGui` event. Fabric applies
and saves the setting on the Minecraft client thread. The backend never owns the
local overlay preference. Existing clients ignore this new response. Deploy the
backend change with the matching Core and Fabric changes for the command to work.

## Local verification

The Dockerfile build target runs the complete NUnit suite and publishes the
application. `TradeGuiCommandTests` checks registration, command update metadata,
argument validation, status, and serialization. `TradeGuiSocketTests` also checks
on, off, and status over a real WebSocket using the inherited MinecraftSocket
message handler and registry.

A fixture is available for the Java Core and Minecraft client:

```sh
docker run --rm --name skycofl-command-fixture \
  -p 127.0.0.1:18084:18084 -e SKYCOFL_PROTOCOL_PORT=18084 \
  sky-mod-commands-test \
  dotnet vstest /build/sky/bin/Debug/net10.0/SkyModCommands.dll \
  --TestCaseFilter:FullyQualifiedName~ExternallyDrivenProtocolFixture
```

Use `ws://127.0.0.1:18084/modsocket` as the Core integration test endpoint.
The fixture skips connection login and external service bootstrap, then uses the
production registry, command handler, response serializer, and WebSocket transport.
It provides no production authentication or service integration evidence. Send
`fixtureShutdown` or stop the exact named container after its final consumer.
The fixture times out after nine minutes and always stops its listener.

Run Minecraft client verification through the laptop's existing Trident CLI with
a matching disposable dedicated server on node-1. Do not modify a personal mod
installation or deploy this test fixture as a public service.
