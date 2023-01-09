using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using OpenTracing;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.MC
{
    public class VerificationHandler
    {
        private MinecraftSocket socket;
        private SessionInfo SessionInfo;

        private DateTime LastVerificationRequest = default;

        public Activity ConSpan { get; }

        public VerificationHandler(MinecraftSocket socket)
        {
            this.socket = socket;
            SessionInfo = socket.SessionInfo;
            ConSpan = socket.ConSpan;
        }


        public async Task MakeSureUserIsVerified(AccountInfo info)
        {
            if (IsLastVerifyRequestRecent())
                return;
            LastVerificationRequest = DateTime.UtcNow;
            var isVerified = await CheckVerificationStatus(info);
            if (!isVerified && info.Tier > 0)
            {
                SendMessage("You have premium but you haven't verified your account yet.");
                await Task.Delay(2000).ConfigureAwait(false);
                SendMessage("You have to verify your account before you receive flips at max speed. See above for how to do that.", null, "This is part of our anti macro system and required to make sure you are not connecting from a cracked account");
            }
        }

        private void SendMessage(string msg, string click = null, string hover = null)
        {
            socket.SendMessage(socket.sessionLifesycle.COFLNET + msg, click, hover);
        }

        public virtual async Task<bool> CheckVerificationStatus(AccountInfo accountInfo)
        {
            using var verificationSpan = socket.CreateActivity("VerificationCheck", ConSpan);
            while (string.IsNullOrEmpty(SessionInfo.McUuid))
                await Task.Delay(500).ConfigureAwait(false);
            var mcUuid = SessionInfo.McUuid;
            var userId = accountInfo.UserId.ToString();
            if (accountInfo.McIds.Contains(SessionInfo.McUuid))
            {
                SessionInfo.VerifiedMc = true;
                verificationSpan.AddTag("verified", "via accountInfo");
                // dispatch access request to update last request time (and keep)
                _ = socket.TryAsyncTimes(() => McAccountService.Instance.ConnectAccount(userId, mcUuid), "", 1);
                return SessionInfo.VerifiedMc;
            }
            McAccountService.ConnectionRequest connect = null;
            for (int i = 0; i < 15; i++)
            {
                if (string.IsNullOrEmpty(mcUuid))
                    mcUuid = SessionInfo.McUuid;
                try
                {
                    connect = await McAccountService.Instance.ConnectAccount(userId, mcUuid);
                    if (connect != null)
                        break;
                }
                catch (System.Exception)
                {

                }
                await Task.Delay(800).ConfigureAwait(false);
                verificationSpan.Log($"failed {userId} {mcUuid} {mcUuid is null}");
            }
            if (connect == null)
            {
                socket.Log("could not get connect result");
                SendMessage(McColorCodes.RED + "We could not verify your account. Please click this to create a report and seek support on the discord server with the id", "/cofl report mcaccount link", "Click to create report\nThis helps us to fix the issue");
                return false;
            }
            if (connect.IsConnected)
            {
                SessionInfo.VerifiedMc = true;
                if (!accountInfo.McIds.Contains(mcUuid))
                    accountInfo.McIds.Add(mcUuid);
                return SessionInfo.VerifiedMc;
            }
            verificationSpan.Log(JSON.Stringify(connect));
            await SendVerificationInstructions(connect);

            return false;
        }

        private async Task SendVerificationInstructions(McAccountService.ConnectionRequest connect)
        {
            var verification = socket.CreateActivity("Verification", ConSpan);
            var bid = connect.Code;
            ItemPrices.AuctionPreview targetAuction = null;
            foreach (var type in new List<string> { "STICK", "RABBIT_HAT", "WOOD_SWORD", "VACCINE_TALISMAN" })
            {
                targetAuction = await NewMethod(bid, type);
                if (targetAuction != null)
                    break;
            }
            verification.SetTag("code", bid);
            verification.Log(JSON.Stringify(targetAuction));

            socket.SendMessage(new ChatPart(
                $"{socket.sessionLifesycle.COFLNET}You connected from an unkown account. Please verify that you are indeed {SessionInfo.McName} by bidding {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on a random auction. ", "/ah"));
            if (targetAuction != null)
                socket.SendMessage(new ChatPart($"{McColorCodes.YELLOW}[CLICK TO {McColorCodes.BOLD}VERIFY{McColorCodes.RESET + McColorCodes.YELLOW} by BIDDING {bid}]", $"/viewauction {targetAuction?.Uuid}",
                $"{McColorCodes.GRAY}Click to open an auction to bid {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}"));
            else
                socket.SendMessage($"Sorry could not find a cheap auction to bid on. You could create an auction yourself for any item you want. The starting bid has to end with {McColorCodes.AQUA}{bid.ToString().PadLeft(3, '0')}{McCommand.DEFAULT_COLOR}");
        }

        private bool IsLastVerifyRequestRecent()
        {
            return LastVerificationRequest > DateTime.UtcNow - TimeSpan.FromSeconds(5);
        }

        private static async Task<ItemPrices.AuctionPreview> NewMethod(int bid, string type)
        {
            var r = new Random();
            var activeAuction = await ItemPrices.Instance.GetActiveAuctions(new ActiveItemSearchQuery()
            {
                name = type,
            });

            var targetAuction = activeAuction.Where(a => a.Price < bid).OrderBy(x => r.Next()).FirstOrDefault();
            return targetAuction;
        }
    }
}
