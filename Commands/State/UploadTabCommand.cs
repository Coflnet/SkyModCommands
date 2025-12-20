using System;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC
{
    public class UploadTabCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            if (arguments.Contains("The Rift"))
                await MinecraftSocket.Commands["uploadscoreboard"].Execute(socket, arguments);

            var youtuberService = socket.GetService<YoutuberService>();
            var fields = this.Convert<string[]>(arguments);
            foreach (var item in fields)
            {
                if(item.StartsWith("Profile: "))
                {
                    socket.SessionInfo.ProfileId = item["Profile: ".Length..];
                }

                // Look for youtube entries like: "[482] [YOUTUBE] Alaawii"
                var tag = "[YOUTUBE]";
                var idx = item.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    // name follows the tag
                    var nameStart = idx + tag.Length;
                    var name = item.Length > nameStart ? item[nameStart..].Trim() : string.Empty;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    try
                    {
                        var existing = await youtuberService.GetYoutuberByNameAsync(name);
                        if (existing == null || string.IsNullOrEmpty(existing.Uuid))
                        {
                            // Resolve uuid (use socket helper which wraps PlayerName service)
                            string uuid = string.Empty;
                            try
                            {
                                uuid = await socket.GetPlayerUuid(name);
                            }
                            catch (Exception)
                            {
                                // If we can't resolve now, skip persisting
                                continue;
                            }

                            await youtuberService.SaveYoutuberAsync(name, uuid);
                        }
                    }
                    catch (Exception)
                    {
                        // Swallow datastore errors here to avoid breaking tab upload flow
                    }
                }
            }
        }
    }
}