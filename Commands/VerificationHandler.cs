using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using System.Diagnostics;
using Coflnet.Sky.Api.Client.Api;

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


        public async Task MakeSureUserIsVerified(AccountInfo info, SessionInfo sessionInfo)
        {
            if (IsLastVerifyRequestRecent())
                return;
            LastVerificationRequest = DateTime.UtcNow;
            var isVerified = await CheckVerificationStatus(info);
            if (!isVerified && sessionInfo.SessionTier >= AccountTier.PREMIUM)
            {
                SendMessage("You have premium but you haven't verified your account yet.");
                await Task.Delay(500).ConfigureAwait(false);
                SendMessage(
                    McColorCodes.YELLOW + "You have to verify your account before you receive flips at max speed. See above for how to do that.", null,
                    "This is part of our anti macro system and required to make sure you are not connecting from a cracked account");
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
            if (accountInfo?.UserId == null)
                return false;
            var mcUuid = SessionInfo.McUuid;
            var userId = accountInfo.UserId.ToString();
            verificationSpan?.AddTag("userId", userId);
            verificationSpan?.AddTag("mcUuid", mcUuid);
            if (accountInfo.McIds.Contains(SessionInfo.McUuid))
            {
                SessionInfo.VerifiedMc = true;
                verificationSpan.AddTag("verified", "via accountInfo");
                // dispatch access request to update last request time (and keep)
                _ = socket.TryAsyncTimes(async () =>
                {
                    var connected = await socket.GetService<McAccountService>().ConnectAccount(userId, mcUuid);
                    if (connected.IsConnected)
                        return;
                    accountInfo.McIds.Remove(mcUuid);
                    await socket.sessionLifesycle.AccountInfo.Update(accountInfo);
                    using var failSpan = socket.CreateActivity("verifyFail", ConSpan);
                    socket.Dialog(db => db.MsgLine("There was an account verification missmatch. Except if you are trying to bypass delay, everything is fine for you. You can't receive tfm balance. Please click this message and then ping Äkwav on the support discord with the printed code.", "/cofl report mcaccount link"));
                    failSpan.AddTag("verified", "missmatch");
                    throw new Exception("Could not connect account");
                }, "", 1);
                return SessionInfo.VerifiedMc;
            }
            McAccountService.ConnectionRequest connect = null;
            for (int i = 0; i < 15; i++)
            {
                if (string.IsNullOrEmpty(mcUuid))
                    mcUuid = SessionInfo.McUuid;
                try
                {
                    connect = await socket.GetService<McAccountService>().ConnectAccount(userId, mcUuid);
                    if (connect != null)
                        break;
                }
                catch (Exception)
                {

                }
                await Task.Delay(800).ConfigureAwait(false);
                verificationSpan.Log($"failed {userId} {mcUuid} {mcUuid is null}");
            }
            if (connect == null || connect.Code == 0)
            {
                socket.Log("could not get connect result");
                SendMessage(McColorCodes.RED + "We could not verify your account. Please click this to create a report and seek support on the discord server with the id", "/cofl report mcaccount link", "Click to create report\nThis helps us to fix the issue");
                return false;
            }
            if (connect.IsConnected)
            {
                SessionInfo.VerifiedMc = true;
                if (!accountInfo.McIds.Contains(mcUuid))
                {
                    accountInfo.McIds.Add(mcUuid);
                    await socket.sessionLifesycle.AccountInfo.Update(accountInfo);
                }
                return SessionInfo.VerifiedMc;
            }
            verificationSpan.Log(JSON.Stringify(connect));
            await socket.TryAsyncTimes(() => SendVerificationInstructions(connect), "sending verification instructions");

            return false;
        }

        private async Task SendVerificationInstructions(McAccountService.ConnectionRequest connect)
        {
            var verification = socket.CreateActivity("Verification", ConSpan);
            var bid = connect.Code;
            Api.Client.Model.AuctionPreview targetAuction = null;
            foreach (var type in new List<string> { "STICK", "RABBIT_HAT", "WOOD_SWORD", "VACCINE_TALISMAN" })
            {
                try
                {
                    targetAuction = await GetauctionToBidOn(bid, type);
                }
                catch (Exception e)
                {
                    socket.Error(e, "Could not get auction to bid on");
                }
                if (targetAuction != null)
                    break;
            }
            verification.SetTag("code", bid);
            verification.Log(JSON.Stringify(targetAuction));

            socket.SendMessage(new ChatPart(
                $"{socket.sessionLifesycle.COFLNET}You connected from an unknown account. Please verify that you are indeed {SessionInfo.McName} by bidding {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on a random auction. ", "/ah"));
            if (targetAuction != null)
            {
                if (socket.SessionInfo.IsMacroBot)
                {
                    await Task.Delay(5000);
                    SaveAuction cheapBin;
                    using (var db = new HypixelContext())
                        cheapBin = db.Auctions.Where(x => x.Bin && x.StartingBid < 1000 && x.HighestBidAmount == 0 && x.End > DateTime.UtcNow && x.Id > db.Auctions.Max(a => a.Id) - 100_000).FirstOrDefault();
                    // autoverify
                    socket.Dialog(db => db.MsgLine("Attempting to autoverify with a pseudo flip."));
                    var circumventTracker = socket.GetService<CircumventTracker>();
                    var flip = FlipperService.LowPriceToFlip(new LowPricedAuction()
                    {
                        Auction = cheapBin,
                        TargetPrice = bid + 1000,
                        DailyVolume = 1,
                        Finder = LowPricedAuction.FinderType.EXTERNAL
                    });
                    flip.Context["match"] = "whitelist shitflip";
                    await circumventTracker.SendChallangeFlip(socket, flip);
                    await Task.Delay(5000);
                    socket.Dialog(db => db.MsgLine("It can take up to 1 minute to verify your account. If you are not verified after that, please try again."));
                }
                else
                    socket.SendMessage(new ChatPart($"{McColorCodes.YELLOW}[CLICK TO {McColorCodes.BOLD}VERIFY{McColorCodes.RESET + McColorCodes.YELLOW} by BIDDING {bid}]", $"/viewauction {targetAuction?.Uuid}",
                    $"{McColorCodes.GRAY}Click to open an auction to bid {McColorCodes.AQUA}{bid}{McCommand.DEFAULT_COLOR} on\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}"));
                SessionInfo.VerificationBidAuctioneer = targetAuction.Seller;
                SessionInfo.VerificationBidAmount = bid;
            }
            else
                socket.SendMessage($"Please create an auction yourself for any item you want. The starting bid has to end with {McColorCodes.AQUA}{bid.ToString().PadLeft(3, '0')}{McCommand.DEFAULT_COLOR}\nyou can also bid another number with the same digits at the end\neg. 1,234,{McColorCodes.AQUA}{bid}");
        }

        private bool IsLastVerifyRequestRecent()
        {
            return LastVerificationRequest > DateTime.UtcNow - TimeSpan.FromSeconds(5);
        }

        private async Task<Api.Client.Model.AuctionPreview> GetauctionToBidOn(int bid, string type, bool bin = false)
        {
            var service = socket.GetService<IAuctionsApi>();
            var options = await service.ApiAuctionsTagItemTagActiveOverviewGetAsync(type, new Dictionary<string, string>()
            {
                {"Bin",bin.ToString() }
            });

            var targetAuction = options.Where(a => a.Price < bid).OrderBy(x => Random.Shared.Next()).FirstOrDefault();
            return targetAuction;
        }
    }
}
