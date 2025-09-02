using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC
{
    /// <summary>
    /// Command to generate API keys for users
    /// </summary>
    [CommandDescription("Generate an API key for accessing external services")]
    public class ApiCommand : McCommand
    {
        public override bool IsPublic => true;

        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            var args = JsonConvert.DeserializeObject<string>(arguments);
            
            if (string.IsNullOrEmpty(socket.SessionInfo?.McUuid))
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}You must be logged in to generate an API key.");
                return;
            }

            try
            {
                var apiKeyService = socket.GetService<ApiKeyService>();
                
                switch (args?.ToLower())
                {
                    case "":
                    case null:
                        await GenerateIfNoneExisting(socket, apiKeyService);
                        break;
                    case "generate":
                    case "create":
                    case "new":
                        await GenerateNewApiKey(socket, apiKeyService);
                        break;
                    
                    case "list":
                    case "show":
                        await ShowUserApiKeys(socket, apiKeyService);
                        break;
                    
                    case "help":
                    default:
                        ShowHelp(socket);
                        break;
                }
            }
            catch (Exception ex)
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}An error occurred while processing your API key request.");
                dev.Logger.Instance.Error(ex, "ApiCommand execution failed");
            }
        }

        private async Task GenerateIfNoneExisting(MinecraftSocket socket, ApiKeyService apiKeyService)
        {
            var sessionLifecycle = socket.sessionLifesycle;

            if (sessionLifecycle?.UserId?.Value == null)
            {
                await socket.SendLoginPrompt();
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}You must be logged in to generate an API key.");
                return;
            }

            var userKeys = await apiKeyService.GetUserApiKeys(sessionLifecycle.UserId.Value);
            var activeKeys = userKeys.Where(k => k.IsActive).ToList();

            if (activeKeys.Any())
            {
                await ShowUserApiKeys(socket, apiKeyService);
                return;
            }

            await GenerateNewApiKey(socket, apiKeyService);
        }

        private async Task GenerateNewApiKey(MinecraftSocket socket, ApiKeyService apiKeyService)
        {
            var sessionInfo = socket.SessionInfo;
            var sessionLifecycle = socket.sessionLifesycle;

            if (sessionLifecycle?.UserId?.Value == null)
            {
                await socket.SendLoginPrompt();
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}You must be logged in to generate an API key.");
                return;
            }

            if (new Version(socket.Version) < new Version(1, 7, 3))
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}This requires mod version 1.7.3 or higher. Please update your mod. {McColorCodes.YELLOW}[click to open page](https://sky.coflnet.com/mod)");
                return;
            }
            
            if (string.IsNullOrEmpty(sessionInfo.ProfileId))
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}Profile ID not available. Please change islands so it can be read from chat and then try again.");
                return;
            }

            try
            {
                var apiKey = await apiKeyService.GenerateApiKey(
                    sessionLifecycle.UserId.Value,
                    sessionInfo.McUuid,
                    sessionInfo.ProfileId,
                    sessionInfo.McName ?? "Unknown"
                );

                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.GREEN}✓ API Key Generated Successfully!")
                    .MsgLine($"{McColorCodes.GRAY}Your new API key:")
                    .MsgLine($"{McColorCodes.YELLOW}{apiKey}", $"copy:{apiKey}", "Click to copy API key")
                    .MsgLine("")
                    .MsgLine($"{McColorCodes.GRAY}Key details:")
                    .MsgLine($"{McColorCodes.AQUA}Minecraft UUID: {McColorCodes.WHITE}{sessionInfo.McUuid}")
                    .MsgLine($"{McColorCodes.AQUA}Stored profile id")
                    .MsgLine($"{McColorCodes.AQUA}Minecraft Name: {McColorCodes.WHITE}{sessionInfo.McName}")
                    .MsgLine("")
                    .MsgLine($"{McColorCodes.RED}⚠ Keep this key secure! Do not share it publicly.")
                    .MsgLine($"{McColorCodes.GRAY}This key expires in 180 days.")
                );
            }
            catch (Exception ex)
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}Failed to generate API key. Please try again later.");
                dev.Logger.Instance.Error(ex, "Failed to generate API key");
            }
        }

        private async Task ShowUserApiKeys(MinecraftSocket socket, ApiKeyService apiKeyService)
        {
            try
            {
                var sessionLifecycle = socket.sessionLifesycle;
                if (sessionLifecycle?.UserId?.Value == null)
                {
                    socket.SendMessage($"{COFLNET}{McColorCodes.RED}You must be logged in to view API keys.");
                    return;
                }

                var userKeys = await apiKeyService.GetUserApiKeys(sessionLifecycle.UserId.Value);
                var activeKeys = userKeys.Where(k => k.IsActive).ToList();

                if (!activeKeys.Any())
                {
                    socket.Dialog(db => db
                        .MsgLine($"{McColorCodes.YELLOW}No active API keys found.")
                        .MsgLine($"{McColorCodes.GRAY}Use {McColorCodes.AQUA}/cofl api generate{McColorCodes.GRAY} to create one.")
                    );
                    return;
                }

                socket.Dialog(db => db
                    .MsgLine($"{McColorCodes.GREEN}Your Active API Keys:")
                    .MsgLine("")
                );

                foreach (var key in activeKeys.Take(5)) // Limit to 5 most recent
                {
                    var keyPreview = key.Key.Substring(0, Math.Min(12, key.Key.Length)) + "...";
                    var createdDate = key.CreatedAt.ToString("yyyy-MM-dd HH:mm");
                    var lastUsedInfo = key.LastUsed?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
                    
                    socket.Dialog(db => db
                        .MsgLine($"{McColorCodes.AQUA}Key: {McColorCodes.WHITE}{keyPreview}", $"/cofl copy {key.Key}", "Click to copy full key")
                        .MsgLine($"{McColorCodes.GRAY}Created: {createdDate}")
                        .MsgLine($"{McColorCodes.GRAY}Last Used: {lastUsedInfo}")
                        .MsgLine($"{McColorCodes.GRAY}Usage Count: {key.UsageCount}")
                        .MsgLine("")
                    );
                }
            }
            catch (Exception ex)
            {
                socket.SendMessage($"{COFLNET}{McColorCodes.RED}Failed to retrieve API keys.");
                dev.Logger.Instance.Error(ex, "Failed to retrieve user API keys");
            }
        }

        private void ShowHelp(MinecraftSocket socket)
        {
            socket.Dialog(db => db
                .MsgLine($"{McColorCodes.GOLD}API Key Commands:")
                .MsgLine("")
                .MsgLine($"{McColorCodes.AQUA}/cofl api generate {McColorCodes.GRAY}- Generate a new API key")
                .MsgLine($"{McColorCodes.AQUA}/cofl api list {McColorCodes.GRAY}- Show your existing API keys")
                .MsgLine("")
                .MsgLine($"{McColorCodes.YELLOW}What are API keys?")
                .MsgLine($"{McColorCodes.GRAY}API keys allow external applications")
                .MsgLine($"{McColorCodes.GRAY}to access SkyMod services on your behalf.")
                .MsgLine("")
                .MsgLine($"{McColorCodes.RED}⚠ Never share your API keys with anyone!")
            );
        }
    }
}
