using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using Xrpl.Client;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Sugar;
using Timer = System.Timers.Timer;


// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/client/subscribe.ts

namespace Xrpl.Tests.ClientLib
{
    [TestClass]
    public class TestUSubscribe
    {

        public static SetupUnitClient runner;

        [TestInitialize]
        public async Task MyTestInitializeAsync()
        {
            runner = await new SetupUnitClient().SetupClient();
        }

        [TestCleanup]
        public async Task MyTestCleanupAsync()
        {
            await runner.client.Disconnect();
        }

        [TestMethod]
        public async Task TestSubscribe()
        {

            string jsonString = "{\"id\":0,\"status\":\"success\",\"type\":\"response\",\"result\":{\"fee_base\":10,\"fee_ref\":10,\"hostid\":\"NAP\",\"ledger_hash\":\"60EBABF55F6AB58864242CADA0B24FBEA027F2426917F39CA56576B335C0065A\",\"ledger_index\":8819951,\"ledger_time\":463782770,\"load_base\":256,\"load_factor\":256,\"pubkey_node\":\"n9Lt7DgQmxjHF5mYJsV2U9anALHmPem8PWQHWGpw4XMz79HA5aJY\",\"random\":\"EECFEE93BBB608914F190EC177B11DE52FC1D75D2C97DACBD26D2DFC6050E874\",\"reserve_base\":20000000,\"reserve_inc\":5000000,\"server_status\":\"full\",\"validated_ledgers\":\"32570-8819951\"}}";
            Dictionary<string, object> jsonData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
            runner.mockedRippled.AddResponse("subscribe", jsonData);
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "command", "subscribe" },
            };
            await runner.client.Request(tx);
        }

        [TestMethod]
        public async Task TestUnsubscribe()
        {

            string jsonString = "{\"id\":0,\"status\":\"success\",\"type\":\"response\",\"result\":{}}";
            Dictionary<string, object> jsonData = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);
            runner.mockedRippled.AddResponse("unsubscribe", jsonData);
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "command", "unsubscribe" },
            };
            await runner.client.Request(tx);
        }

        [TestMethod]
        public void TestEmitsTransaction()
        {
            bool isDone = false;
            runner.client.connection.OnTransaction += r =>
            {
                Assert.AreEqual(ResponseStreamType.transaction, r.Type);
                isDone = true;
                return Task.CompletedTask;
            };

            string jsonString = "{\"engine_result\":\"tesSUCCESS\",\"engine_result_code\":0,\"engine_result_message\":\"Thetransactionwasapplied.Onlyfinalinavalidatedledger.\",\"ledger_hash\":\"922099A5528EFDF820ABFAB0CAAB8647DF6E7103B3BA8CDD3A6D56EAF1B39B3B\",\"ledger_index\":66093882,\"meta\":{\"AffectedNodes\":[{\"DeletedNode\":{\"FinalFields\":{\"Account\":\"rnruxxLTbJUMNtFNBJ7X2xSiy1KE7ajUuH\",\"BookDirectory\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E3E58B40BAC52\",\"BookNode\":\"0\",\"Flags\":0,\"OwnerNode\":\"0\",\"PreviousTxnID\":\"E3E2E94FE181C5F5E03E2FE5347C4E8E27E18290FF3B7FA6BA9B124AD54F147D\",\"PreviousTxnLgrSeq\":66093873,\"Sequence\":18466973,\"TakerGets\":\"9416365482\",\"TakerPays\":{\"currency\":\"CNY\",\"issuer\":\"rJ1adrpGS3xsnQMb9Cw54tWJVFPuSdZHK\",\"value\":\"80159.63607543072\"}},\"LedgerEntryType\":\"Offer\",\"LedgerIndex\":\"3A93F99B4CB2F4FB0F4F5182E85C37855611E6470262DF63896B6E0AA4231AE0\"}},{\"CreatedNode\":{\"LedgerEntryType\":\"DirectoryNode\",\"LedgerIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\",\"NewFields\":{\"ExchangeRate\":\"4f1e2bd6998872d5\",\"RootIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\",\"TakerPaysCurrency\":\"000000000000000000000000434E590000000000\",\"TakerPaysIssuer\":\"0360E3E0751BD9A566CD03FA6CAFC78118B82BA0\"}}},{\"DeletedNode\":{\"FinalFields\":{\"ExchangeRate\":\"4f1e3e58b40bac52\",\"Flags\":0,\"RootIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E3E58B40BAC52\",\"TakerGetsCurrency\":\"0000000000000000000000000000000000000000\",\"TakerGetsIssuer\":\"0000000000000000000000000000000000000000\",\"TakerPaysCurrency\":\"000000000000000000000000434E590000000000\",\"TakerPaysIssuer\":\"0360E3E0751BD9A566CD03FA6CAFC78118B82BA0\"},\"LedgerEntryType\":\"DirectoryNode\",\"LedgerIndex\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E3E58B40BAC52\"}},{\"CreatedNode\":{\"LedgerEntryType\":\"Offer\",\"LedgerIndex\":\"8934A20864E420B7D0F6CDC61F5D8D2E609DEB8E25D3CB26A1B595032483A4C8\",\"NewFields\":{\"Account\":\"rnruxxLTbJUMNtFNBJ7X2xSiy1KE7ajUuH\",\"BookDirectory\":\"623C4C4AD65873DA787AC85A0A1385FE6233B6DE100799474F1E2BD6998872D5\",\"Sequence\":18466977,\"TakerGets\":\"8221180253\",\"TakerPays\":{\"currency\":\"CNY\",\"issuer\":\"rJ1adrpGS3xsnQMb9Cw54tWJVFPuSdZHK\",\"value\":\"69817.9622410017\"}}}},{\"ModifiedNode\":{\"FinalFields\":{\"Account\":\"rnruxxLTbJUMNtFNBJ7X2xSiy1KE7ajUuH\",\"Balance\":\"5116214416\",\"Flags\":0,\"OwnerCount\":5,\"Sequence\":18466978},\"LedgerEntryType\":\"AccountRoot\",\"LedgerIndex\":\"9AC13F682F58D555C134D098EEEE1A14BECB904C65ACBBB0046B35B405E66A75\",\"PreviousFields\":{\"Balance\":\"5116214428\",\"Sequence\":18466977},\"PreviousTxnID\":\"48DF68A5C9D50C2CB2FE750E3D3A40B041FDD12FD2185DF4F97B2A0CA379DCB0\",\"PreviousTxnLgrSeq\":66093873}},{\"ModifiedNode\":{\"FinalFields\":{\"Flags\":0,\"Owner\":\"rnruxxLTbJUMNtFNBJ7X2xSiy1KE7ajUuH\",\"RootIndex\":\"FBD0BC6A9DCBC5AEFB9C773EE6351AF11E244DBD1370EDF6801FD607F01D3DF8\"},\"LedgerEntryType\":\"DirectoryNode\",\"LedgerIndex\":\"FBD0BC6A9DCBC5AEFB9C773EE6351AF11E244DBD1370EDF6801FD607F01D3DF8\"}}],\"TransactionIndex\":40,\"TransactionResult\":\"tesSUCCESS\"},\"status\":\"closed\",\"transaction\":{\"Account\":\"rnruxxLTbJUMNtFNBJ7X2xSiy1KE7ajUuH\",\"Fee\":\"12\",\"Flags\":0,\"LastLedgerSequence\":66093884,\"OfferSequence\":18466973,\"Sequence\":18466977,\"SigningPubKey\":\"026B8A4318970123B0BB3DC528C4DA62C874AD4A01F399DBEF21D621DDC32F6C81\",\"TakerGets\":\"8221180253\",\"TakerPays\":{\"currency\":\"CNY\",\"issuer\":\"rJ1adrpGS3xsnQMb9Cw54tWJVFPuSdZHK\",\"value\":\"69817.9622410017\"},\"TransactionType\":\"OfferCreate\",\"TxnSignature\":\"304402200E0821A9FC8A0A7CA72DC0CEC3BD2AC1317A8DCFAAE1F27EB7C69C79EB475DD3022046BBFA7DFAD9B7186CAEA798358C0959014B27B2B1EF3D6CCEF5EC0EA346D692\",\"date\":683942752,\"hash\":\"775266C42CED11D5FC6DB61686177FCEA689E7A79E6B0017586E95FA3E9EDD10\",\"owner_funds\":\"5071214380\"},\"type\":\"transaction\",\"validated\":true}";
            runner.client.connection.OnMessage(jsonString);

            while (isDone == false)
            {
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        [TestMethod]
        public void TestEmitsLedger()
        {
            runner.client.connection.OnLedgerClosed += r =>
            {
                //Assert.IsTrue(r.Type == ResponseStreamType.ledgerClosed);
                return Task.CompletedTask;
            };

            string jsonString = "{\"fee_base\":10,\"fee_ref\":10,\"ledger_hash\":\"B3980C722D71873D6708723E71B7A28C826BC66C58712ADCEC61603415305CD1\",\"ledger_index\":66093872,\"ledger_time\":683942720,\"reserve_base\":20000000,\"reserve_inc\":5000000,\"txn_count\":70,\"type\":\"ledgerClosed\",\"validated_ledgers\":\"65201743-66093872\"}";
            runner.client.connection.OnMessage(jsonString);
        }

        [TestMethod]
        public void TestEmitsPeerStatusChange()
        {
            runner.client.connection.OnPeerStatusChange += r =>
            {
                Assert.AreEqual(ResponseStreamType.consensusPhase, r.Type);
                return Task.CompletedTask;
            };

            string jsonString = "{\"action\":\"CLOSING_LEDGER\",\"date\":508546525,\"ledger_hash\":\"4D4CD9CD543F0C1EF023CC457F5BEFEA59EEF73E4552542D40E7C4FA08D3C320\",\"ledger_index\":18853106,\"ledger_index_max\":18853106,\"ledger_index_min\":18852082,\"type\":\"peerStatusChange\"}";
            runner.client.connection.OnMessage(jsonString);
        }

        [TestMethod]
        public async Task TestEmitsPathFind()
        {
            List<PathFindStream> received = new List<PathFindStream>();
            var tcs = new TaskCompletionSource<bool>();

            runner.client.connection.OnPathFind += r =>
            {
                received.Add(r);
                if (received.Count >= 2)
                    tcs.TrySetResult(true);
                return Task.CompletedTask;
            };

            string msg1 = "{\"alternatives\":[{\"paths_computed\":[[{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"type\":48}],[{\"currency\":\"USD\",\"issuer\":\"rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq\",\"type\":48},{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"type\":48}],[{\"currency\":\"USD\",\"issuer\":\"rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq\",\"type\":48},{\"account\":\"rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq\",\"type\":1},{\"account\":\"rpix35SSFEukMTm64NB4k4BPBS7fXJrLJM\",\"type\":1}],[{\"currency\":\"CNY\",\"issuer\":\"rKiCet8SdvWxPXnAgYarFUXMh1zCPz432Y\",\"type\":48},{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"type\":48}]],\"source_amount\":\"786\"}],\"destination_account\":\"rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn\",\"destination_amount\":{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"value\":\"0.001\"},\"full_reply\":false,\"id\":8,\"source_account\":\"rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn\",\"type\":\"path_find\"}";

            string msg2 = "{\"alternatives\":[{\"paths_computed\":[[{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"type\":48}]],\"source_amount\":\"400\"}],\"destination_account\":\"rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn\",\"destination_amount\":{\"currency\":\"USD\",\"issuer\":\"rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B\",\"value\":\"0.001\"},\"full_reply\":true,\"id\":8,\"source_account\":\"rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn\",\"type\":\"path_find\"}";

            await runner.client.connection.OnMessage(msg1);
            await runner.client.connection.OnMessage(msg2);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, completed, "OnPathFind was not invoked at least 2 times within timeout");

            Assert.AreEqual(2, received.Count);

            PathFindStream first = received[0];
            Assert.AreEqual(ResponseStreamType.path_find, first.Type);
            Assert.AreEqual("rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", first.SourceAccount);
            Assert.AreEqual("rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", first.DestinationAccount);
            Assert.IsFalse(first.FullReply);
            Assert.AreEqual(1, first.Alternatives.Count);
            Assert.AreEqual("786", first.Alternatives[0].SourceAmount.Value);

            PathFindStream second = received[1];
            Assert.AreEqual(ResponseStreamType.path_find, second.Type);
            Assert.IsTrue(second.FullReply);
            Assert.AreEqual(1, second.Alternatives.Count);
            Assert.AreEqual("400", second.Alternatives[0].SourceAmount.Value);
        }

        [TestMethod]
        public async Task TestEmitsValidationReceived()
        {
            var tcs = new TaskCompletionSource<ValidationStream>();

            runner.client.connection.OnValidationReceived += r =>
            {
                tcs.TrySetResult(r);
                return Task.CompletedTask;
            };

            string jsonString = "{\"type\":\"validationReceived\",\"amendments\":[\"42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE\",\"4C97EBA926031A7CF7D7B36FDE3ED66DDA5421192D63DE53FFB46E43B9DC8373\",\"6781F8368C4771B83E8B821D88F580202BCB4228075297B19E4FDC5233F1EFDC\",\"C1B8D934087225F509BEB5A8EC24447854713EE447D277F69545ABFA0E0FD490\",\"DA1BD556B42D85EA9C84066D028D355B52416734D3283F85E216EA5DA6DB7E13\"],\"base_fee\":10,\"flags\":2147483649,\"full\":true,\"ledger_hash\":\"EC02890710AAA2B71221B0D560CFB22D64317C07B7406B02959AD84BAD33E602\",\"ledger_index\":\"6\",\"load_fee\":256000,\"master_key\":\"nHUon2tpyJEHHYGmxqeGu37cvPYHzrMtUNQFVdCgGNvEkjmCpTqK\",\"reserve_base\":20000000,\"reserve_inc\":5000000,\"signature\":\"3045022100E199B55643F66BC6B37DBC5E185321CF952FD35D13D9E8001EB2564FFB94A07602201746C9A4F7A93647131A2DEB03B76F05E426EC67A5A27D77F4FF2603B9A528E6\",\"signing_time\":515115322,\"validation_public_key\":\"n94Gnc6svmaPPRHUAyyib1gQUov8sYbjLoEwUBYPH39qHZXuo8ZT\"}";
            await runner.client.connection.OnMessage(jsonString);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, completed, "OnValidationReceived was not invoked within timeout");

            ValidationStream result = tcs.Task.Result;
            Assert.AreEqual(ResponseStreamType.validationReceived, result.Type);
            Assert.AreEqual("nHUon2tpyJEHHYGmxqeGu37cvPYHzrMtUNQFVdCgGNvEkjmCpTqK", result.MasterKey);
            Assert.AreEqual("EC02890710AAA2B71221B0D560CFB22D64317C07B7406B02959AD84BAD33E602", result.LedgerHash);
            Assert.AreEqual("n94Gnc6svmaPPRHUAyyib1gQUov8sYbjLoEwUBYPH39qHZXuo8ZT", result.ValidationPublicKey);
            Assert.IsTrue(result.Full);
        }

        [TestMethod]
        public async Task TestEmitsManifestReceived()
        {
            var tcs = new TaskCompletionSource<ManifestStream>();

            runner.client.connection.OnManifestReceived += r =>
            {
                tcs.TrySetResult(r);
                return Task.CompletedTask;
            };

            string jsonString = "{\"type\":\"manifestReceived\",\"master_key\":\"nHUon2tpyJEHHYGmxqeGu37cvPYHzrMtUNQFVdCgGNvEkjmCpTqK\",\"master_signature\":\"3045022100AABBCCDD\",\"seq\":42,\"signing_key\":\"n94Gnc6svmaPPRHUAyyib1gQUov8sYbjLoEwUBYPH39qHZXuo8ZT\",\"signature\":\"3045022100EEFF0011\",\"domain\":\"example.com\"}";
            await runner.client.connection.OnMessage(jsonString);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, completed, "OnManifestReceived was not invoked within timeout");

            ManifestStream result = tcs.Task.Result;
            Assert.AreEqual(ResponseStreamType.manifestReceived, result.Type);
            Assert.AreEqual("nHUon2tpyJEHHYGmxqeGu37cvPYHzrMtUNQFVdCgGNvEkjmCpTqK", result.MasterKey);
            Assert.AreEqual("3045022100AABBCCDD", result.MasterSignature);
            Assert.AreEqual(42u, result.Seq);
            Assert.AreEqual("n94Gnc6svmaPPRHUAyyib1gQUov8sYbjLoEwUBYPH39qHZXuo8ZT", result.SigningKey);
            Assert.AreEqual("3045022100EEFF0011", result.Signature);
            Assert.AreEqual("example.com", result.Domain);
        }

        [TestMethod]
        public async Task TestEmitsBookChanges()
        {
            var tcs = new TaskCompletionSource<BookChangesStream>();

            runner.client.connection.OnBookChanges += r =>
            {
                tcs.TrySetResult(r);
                return Task.CompletedTask;
            };

            string jsonString = "{\"type\":\"bookChanges\",\"ledger_index\":104205117,\"ledger_hash\":\"A54D8DFCE7DD4770116B3EA2CC5B78DB3B1FC1CE01541C0DD691DCBA0673F6C6\",\"ledger_time\":832003272,\"validated\":true,\"changes\":[{\"currency_a\":\"XRP_drops\",\"currency_b\":\"rDSkXt9C1fdGrgMoajRgM1SkGwHLy6Ckme/4C45535300000000000000000000000000000000\",\"volume_a\":\"636084\",\"volume_b\":\"2148.917370490562\",\"high\":\"296.0020746887967\",\"low\":\"296.0020746887967\",\"open\":\"296.0020746887967\",\"close\":\"296.0020746887967\"}]}";
            await runner.client.connection.OnMessage(jsonString);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, completed, "OnBookChanges was not invoked within timeout");

            BookChangesStream result = tcs.Task.Result;
            Assert.AreEqual(ResponseStreamType.bookChanges, result.Type);
            Assert.AreEqual(104205117u, result.LedgerIndex);
            Assert.AreEqual("A54D8DFCE7DD4770116B3EA2CC5B78DB3B1FC1CE01541C0DD691DCBA0673F6C6", result.LedgerHash);
            Assert.IsNotNull(result.LedgerTime);
            // 832003272 seconds since Ripple Epoch (2000-01-01) = 2026-05-13T...
            Assert.IsTrue(result.LedgerTime.Value.Year == 2026);
            Assert.AreEqual(true, result.Validated);
            Assert.IsNotNull(result.Changes);
            Assert.AreEqual(1, result.Changes.Count);

            BookChange change = result.Changes[0];
            Assert.AreEqual("XRP_drops", change.CurrencyA);
            Assert.AreEqual("rDSkXt9C1fdGrgMoajRgM1SkGwHLy6Ckme/4C45535300000000000000000000000000000000", change.CurrencyB);
            Assert.AreEqual("636084", change.VolumeA);
            Assert.AreEqual("2148.917370490562", change.VolumeB);
            Assert.AreEqual("296.0020746887967", change.High);
            Assert.AreEqual("296.0020746887967", change.Low);
            Assert.AreEqual("296.0020746887967", change.Open);
            Assert.AreEqual("296.0020746887967", change.Close);

            // Verify parsed IssuedCurrency objects
            Assert.IsNotNull(change.AssetA);
            Assert.AreEqual("XRP", change.AssetA.Currency);
            Assert.IsTrue(change.AssetA.IsXrp());
            Assert.IsTrue(change.IsXrpA);
            Assert.IsFalse(change.IsXrpB);

            Assert.IsNotNull(change.AssetB);
            Assert.AreEqual("rDSkXt9C1fdGrgMoajRgM1SkGwHLy6Ckme", change.AssetB.Issuer);
            Assert.AreEqual("4C45535300000000000000000000000000000000", change.AssetB.Currency);
            // ToString() decodes hex → readable name ("LESS")
            Assert.AreEqual("LESS", change.AssetB.ToString());
        }

        [TestMethod]
        public async Task StreamHandlerException_IsSurfacedViaOnError()
        {
            TaskCompletionSource<string> errorReported = new TaskCompletionSource<string>();

            runner.client.connection.OnLedgerClosed += _ =>
                throw new InvalidOperationException("consumer handler bug");

            runner.client.connection.OnError += (error, errorMessage, message, data) =>
            {
                errorReported.TrySetResult(errorMessage);
                return Task.CompletedTask;
            };

            string jsonString = "{\"fee_base\":10,\"fee_ref\":10,\"ledger_hash\":\"B3980C722D71873D6708723E71B7A28C826BC66C58712ADCEC61603415305CD1\",\"ledger_index\":66093872,\"ledger_time\":683942720,\"reserve_base\":20000000,\"reserve_inc\":5000000,\"txn_count\":70,\"type\":\"ledgerClosed\",\"validated_ledgers\":\"65201743-66093872\"}";
            await runner.client.connection.OnMessage(jsonString);

            Task completed = await Task.WhenAny(errorReported.Task, Task.Delay(5000));
            Assert.AreEqual(errorReported.Task, completed,
                "Exception thrown by a stream handler was swallowed instead of surfaced via OnError");
        }

        [TestMethod]
        public async Task TestEmitsServerStatus()
        {
            var tcs = new TaskCompletionSource<ServerStatusStream>();

            runner.client.connection.OnServerStatus += r =>
            {
                tcs.TrySetResult(r);
                return Task.CompletedTask;
            };

            string jsonString = "{\"type\":\"serverStatus\",\"load_base\":256,\"load_factor\":256,\"load_factor_fee_escalation\":512,\"load_factor_fee_queue\":256,\"load_factor_fee_reference\":256,\"load_factor_server\":256,\"server_status\":\"full\",\"base_fee\":10,\"reserve_base\":20000000,\"reserve_inc\":5000000}";
            await runner.client.connection.OnMessage(jsonString);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000));
            Assert.AreEqual(tcs.Task, completed, "OnServerStatus was not invoked within timeout");

            ServerStatusStream result = tcs.Task.Result;
            Assert.AreEqual(ResponseStreamType.serverStatus, result.Type);
            Assert.AreEqual(256u, result.LoadBase);
            Assert.AreEqual(256u, result.LoadFactor);
            Assert.AreEqual(512u, result.LoadFactorFeeEscalation);
            Assert.AreEqual(256u, result.LoadFactorFeeQueue);
            Assert.AreEqual(256u, result.LoadFactorFeeReference);
            Assert.AreEqual(256u, result.LoadFactorServer);
            Assert.AreEqual("full", result.ServerStatus);
            Assert.AreEqual(10u, result.BaseFee);
            Assert.AreEqual(20000000u, result.ReserveBase);
            Assert.AreEqual(5000000u, result.ReserveInc);
        }
    }

    [TestClass]
    public class TestSSubscribe
    {

        [TestMethod]
        public async Task TestSubscribe()
        {
            bool isTested = false;
            bool isFinished = false;

            var server = "wss://s1.ripple.com/";

            var client = new XrplClient(server);

            client.connection.OnConnected += () =>
            {
                Console.WriteLine("CONNECTED");
                return Task.CompletedTask;
            };

            client.connection.OnDisconnect += (code, description) =>
            {
                Console.WriteLine($"Disconnected from XRPL with code: {code}, description: {description}");
                isFinished = true;
                return Task.CompletedTask;
            };

            client.connection.OnLedgerClosed += (message) =>
            {
                Console.WriteLine($"MESSAGE RECEIVED: {message}");
                //Dictionary<string, object> json = JsonConvert.DeserializeObject<Dictionary<string, object>>(message);
                //if (message["type"] == "ledgerClosed")
                //{
                //    isTested = true;
                //    isFinished = true;
                //}
                isTested = true;
                isFinished = true;
                return Task.CompletedTask;
            };

            Timer timer = new Timer(7000);
            timer.Elapsed += async (sender, e) =>
            {
                await client.Disconnect();
                isFinished = true;
            };
            timer.Start();

            //client.connection.ws = client.connection.CreateWebSocket(server, null);

            //_ = client.connection.ws.ConnectAsync();

            //while (!client.connection.ws._isConnected)
            //{
            //    Debug.WriteLine($"CONNECTING... {DateTime.Now}");
            //    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
            //}

            await client.connection.Connect(CancellationToken.None);

            //var subscribe = await client.Subscribe(
            //new SubscribeRequest()
            //{
            //    Streams = new List<string>(new[]
            //    {
            //        "ledger",
            //    })
            //});
            var request = new SubscribeRequest()
            {
                Streams = new List<StreamType>(new[]
                    {
                        StreamType.Ledger,
                    })
            };
            string jsonString = JsonSerializer.Serialize(request, XrplJsonOptions.Default);
            client.connection.WebsocketSendAsync(client.connection.ws, jsonString);

            Debug.WriteLine($"BEFORE: {DateTime.Now}");

            while (!isFinished)
            {
                Debug.WriteLine($"WAITING: {DateTime.Now}");
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            Debug.WriteLine($"AFTER: {DateTime.Now}");
            Debug.WriteLine($"IS FINISHED: {isFinished}");
            Debug.WriteLine($"IS TESTER: {isTested}");
            Assert.IsTrue(isTested);
        }
    }
}

