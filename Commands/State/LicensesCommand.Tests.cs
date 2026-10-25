using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.Settings.Client.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class LicensesCommandTests
{
    private const string UserId = "42";
    private const string AltUuid = "0123456789abcdef0123456789abcdef";
    private static readonly PurchaseableProduct SlotPackage = new(id: 5, title: "Premium+ slot", slug: "premium_plus-slot",
        cost: 1800, ownershipSeconds: 604800, slotCount: 1, slotTier: "premium_plus");

    [Test]
    public async Task AddBuysNewTierSlotAndAssignsItToTheAccount()
    {
        var slots = new Mock<ITierSlotsApi>();
        slots.Setup(a => a.ApiTierSlotsProductsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([SlotPackage]);
        slots.SetupSequence(a => a.ApiTierSlotsOwnerOwnerIdGetAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new TierSlotAccess(id: 7, ownerId: UserId, tier: "premium_plus", expires: DateTime.UtcNow.AddDays(7), varVersion: 1, canManage: true)]);
        var users = LegacyFreeUserApi();
        var socket = new LicenseSocket(slots.Object, users.Object);

        await new LicensesCommand().Execute(socket, JsonConvert.SerializeObject($"add Alt premium_plus-slot {socket.SessionInfo.ConnectionId}"));

        users.Verify(a => a.UserUserIdServicePurchaseProductSlugPostAsync(UserId, "premium_plus-slot", It.IsAny<string>(), 1, It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(a => a.UserUserIdTransferPostAsync(It.IsAny<string>(), It.IsAny<TransferRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "Licenses must no longer be bought on a virtual user");
        slots.Verify(a => a.ApiTierSlotsOwnerOwnerIdIdAssignmentPutAsync(UserId, 7,
            It.Is<TierSlotAssignment>(t => t.MinecraftUuid == AltUuid && t.VarVersion == 1), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task AddExtendsTheSlotAlreadyAssignedToTheAccount()
    {
        var slots = new Mock<ITierSlotsApi>();
        slots.Setup(a => a.ApiTierSlotsProductsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([SlotPackage]);
        slots.Setup(a => a.ApiTierSlotsOwnerOwnerIdGetAsync(UserId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TierSlotAccess(id: 9, ownerId: UserId, tier: "premium_plus", expires: DateTime.UtcNow.AddDays(2), minecraftUuid: AltUuid, varVersion: 3, canManage: true)]);
        var users = LegacyFreeUserApi();
        var socket = new LicenseSocket(slots.Object, users.Object);

        await new LicensesCommand().Execute(socket, JsonConvert.SerializeObject($"add Alt premium_plus-slot {socket.SessionInfo.ConnectionId}"));

        users.Verify(a => a.UserUserIdServicePurchaseDeclaredProductSlugPostAsync(UserId, "premium_plus-slot",
            It.Is<ServicePurchaseRequest>(r => r.SlotIds.Count == 1 && r.SlotIds[0] == 9 && r.RequestId != null), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        users.Verify(a => a.UserUserIdServicePurchaseProductSlugPostAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never,
            "An account that already has a slot of this tier gets it extended instead of a parallel slot");
    }

    /// <summary>
    /// The legacy flow bought licenses by transferring coins to a virtual user first;
    /// rejecting that transfer lets it end before any slot purchase is asserted.
    /// </summary>
    private static Mock<IUserApi> LegacyFreeUserApi()
    {
        var users = new Mock<IUserApi>();
        users.Setup(a => a.UserUserIdTransferPostAsync(It.IsAny<string>(), It.IsAny<TransferRequest>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Payments.Client.Client.ApiException(400, "Error calling UserUserIdTransferPost: {\"message\":\"virtual user transfers are not part of slot purchases\"}"));
        return users;
    }

    private sealed class LicenseSocket : MinecraftSocket
    {
        private readonly ITierSlotsApi slots;
        private readonly IUserApi users;

        public LicenseSocket(ITierSlotsApi slots, IUserApi users)
        {
            this.slots = slots;
            this.users = users;
            sessionLifesycle = new ModSessionLifesycle(this)
            {
                UserId = SelfUpdatingValue<string>.CreateNoUpdate(LicensesCommandTests.UserId),
                AccountInfo = SelfUpdatingValue<AccountInfo>.CreateNoUpdate(new AccountInfo { UserId = LicensesCommandTests.UserId })
            };
        }

        public override T GetService<T>()
        {
            if (typeof(T) == typeof(ITierSlotsApi))
                return (T)slots;
            if (typeof(T) == typeof(IUserApi))
                return (T)users;
            if (typeof(T) == typeof(SettingsService))
                return (T)(object)LicenseSettings();
            if (typeof(T) == typeof(IProductsApi))
                return (T)LegacyProducts();
            return Mock.Of<T>();
        }

        private static SettingsService LicenseSettings()
        {
            var api = new Mock<ISettingsApi>();
            api.Setup(a => a.GetSettingWithHttpInfoAsync(LicensesCommandTests.UserId, "licenses", It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new Coflnet.Sky.Settings.Client.Client.ApiResponse<string>(HttpStatusCode.OK, JsonConvert.SerializeObject(new LicenseSetting())));
            return new SettingsService(Mock.Of<IConfiguration>(), NullLogger<SettingsService>.Instance, api.Object);
        }

        private static IProductsApi LegacyProducts()
        {
            var products = new Mock<IProductsApi>();
            products.Setup(a => a.ProductsPProductSlugGetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PurchaseableProduct(id: 1, title: "Premium+", slug: SlotPackage.Slug, cost: 1800, ownershipSeconds: 604800));
            return products.Object;
        }

        public override Task<string> GetPlayerUuid(string name, bool blockError = false) => Task.FromResult(AltUuid);

        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
        }
    }
}
