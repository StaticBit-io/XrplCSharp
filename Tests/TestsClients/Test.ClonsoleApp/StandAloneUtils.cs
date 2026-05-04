using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace MyApp;

public static class StandAloneUtils
{

    private static string masterAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private static string masterSecret = "snoPBrXtMeMyMHUVTgbuqAfg1SUTb";

    public static async Task LedgerAccept(IXrplClient client)
    {
        var request = new BaseRequest { Command = "ledger_accept" };
        await client.AnyRequest(request);
    }

    public static async Task FundAccount(IXrplClient client, params XrplWallet[] wallets)
    {
        foreach (var wallet in wallets)
        {
            await FundAccount(client, wallet);
        }

    }
    public static async Task FundAccount(IXrplClient client, XrplWallet wallet)
    {
        Payment payment = new Payment
        {
            Account = masterAccount,
            Destination = wallet.ClassicAddress,
            Amount = new Currency { Value = "400000000", CurrencyCode = "XRP" }
        };
        var values = payment.ToDictionary();
        var master = XrplWallet.FromSeed(masterSecret);
        Submit response = await client.Submit(values, master);
        if (response.EngineResult != "tesSUCCESS")
        {
            throw new XrplException($"Response not successful, {response.EngineResult}");
        }
        await LedgerAccept(client);
    }

    public static async Task<XrplWallet> GenerateFundedWallet(IXrplClient client)
    {
        XrplWallet wallet = XrplWallet.Generate();
        await FundAccount(client, wallet);
        return wallet;
    }
}