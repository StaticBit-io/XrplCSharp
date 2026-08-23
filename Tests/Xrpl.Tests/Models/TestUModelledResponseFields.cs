using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Models;

/// <summary>
/// Fields the node sends and no model declared: they now land on typed properties instead of in
/// <c>UnknownFields</c>.
/// </summary>
/// <remarks>
/// Unknown-field capture made the loss visible rather than silent - before it, these vanished
/// between the socket and the caller. Capture is the safety net; declaring them is the fix, and a
/// field counts as done only when it is both a declared property <b>and</b> gone from
/// <c>UnknownFields</c>. Each test here asserts both halves, because either alone can pass while
/// the other fails: a property can be declared under a name the node never sends, and a field can
/// leave the capture by being dropped rather than by being modelled.
/// <para>
/// The JSON is what a node actually answered, not what the documentation says it answers. The
/// types came from the same place: <c>server_state_duration_us</c> is a string in
/// <c>server_info</c> while the same field is a number in <c>server_state</c>, and only asking a
/// node tells you that.
/// </para>
/// </remarks>
[TestClass]
public class TestUModelledResponseFields
{
    /// <summary>
    /// An answer from rippled 3.3.0, trimmed of nothing that matters.
    /// </summary>
    private const string ServerInfoResult = """
    {
      "build_version": "3.3.0",
      "complete_ledgers": "2-14",
      "git": { "branch": "release-3.3", "hash": "00a178fb92ca49521b937ae1a99d863765ea8a90" },
      "hostid": "34d90e50bf06",
      "initial_sync_duration_us": "90874",
      "io_latency_ms": 1,
      "jq_trans_overflow": "0",
      "last_close": { "converge_time_s": 0.1, "proposers": 0 },
      "load": { "job_types": [ { "avg_time": 1, "in_progress": 1, "job_type": "clientRPC", "peak_time": 6 } ], "threads": 1 },
      "load_factor": 1,
      "network_id": 0,
      "node_size": "small",
      "peer_disconnects": "0",
      "peer_disconnects_resources": "0",
      "peers": 0,
      "ports": [
        { "port": "5005", "protocol": [ "http" ] },
        { "port": "6006", "protocol": [ "ws" ] }
      ],
      "pubkey_node": "n9LQdAUHHBnkKFpSG296p2V1xWQB8zJEeWuZx4SUVXWxQMNbZ92e",
      "pubkey_validator": "n9LEa2A2wpov7XaXC8XXiJwknbXUt29M27CWLWz4XzFSXd45eah8",
      "server_state": "proposing",
      "server_state_duration_us": "25380868",
      "state_accounting": { "connected": { "duration_us": "0", "transitions": "0" } },
      "time": "2026-Aug-23 20:02:52.668044 UTC",
      "uptime": 25,
      "validated_ledger": { "age": 1, "base_fee_xrp": 1e-05, "seq": 14 },
      "validation_quorum": 1,
      "validator_list": { "count": 0, "expiration": "unknown", "status": "unknown" }
    }
    """;

    private static IEnumerable<string> Captured(Dictionary<string, JsonElement> unknown) =>
        unknown is null ? Enumerable.Empty<string>() : unknown.Keys.OrderBy(k => k);

    /// <summary>
    /// Ten fields on <c>server_info</c>'s <c>info</c> that no model declared.
    /// </summary>
    /// <remarks>
    /// The report named seven. Measuring against a node found three more - <c>git</c>,
    /// <c>node_size</c> and <c>validator_list</c> - which is why this asserts on the whole capture
    /// being empty rather than on a list of names: a list would have been the list from the report,
    /// and would have missed exactly the ones nobody thought of.
    /// </remarks>
    [TestMethod]
    public void TestUServerInfoDeclaresEverythingTheNodeSends()
    {
        Info info = JsonSerializer.Deserialize<Info>(ServerInfoResult, XrplJsonOptions.Default);

        Assert.IsNotNull(info);
        CollectionAssert.AreEqual(
            new List<string>(),
            Captured(info.UnknownFields).ToList(),
            "server_info sent members that no property claims");

        Assert.AreEqual("90874", info.InitialSyncDurationUs);
        Assert.AreEqual("0", info.JqTransOverflow);
        Assert.AreEqual("0", info.PeerDisconnects);
        Assert.AreEqual("0", info.PeerDisconnectsResources);
        Assert.AreEqual("25380868", info.ServerStateDurationUs);
        Assert.AreEqual("2026-Aug-23 20:02:52.668044 UTC", info.Time);
        Assert.AreEqual("small", info.NodeSize);

        Assert.AreEqual("release-3.3", info.Git?.Branch);
        Assert.AreEqual("unknown", info.ValidatorList?.Status);
        Assert.AreEqual(0, info.ValidatorList?.Count);

        Assert.AreEqual(2, info.Ports?.Count);
        Assert.AreEqual("5005", info.Ports?[0].Port);
        CollectionAssert.AreEqual(new List<string> { "http" }, info.Ports?[0].Protocol);
        CollectionAssert.AreEqual(new List<string> { "ws" }, info.Ports?[1].Protocol);
    }

    /// <summary>
    /// <c>account_lines</c> was the one sibling that never declared <c>validated</c>.
    /// </summary>
    /// <remarks>
    /// rippled writes it through <c>lookupLedger</c> unconditionally, so it arrives on every
    /// answer - a caller simply had no typed way to tell a validated answer from a provisional one.
    /// </remarks>
    [TestMethod]
    public void TestUAccountLinesDeclaresValidated()
    {
        const string json = """
        {
          "account": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
          "ledger_hash": "1D5B45B3FD6E1895D8FB455DBD0BAB0D726B7F1FBB33E0DA24E9CDFC1AB4B26D",
          "ledger_index": 14,
          "lines": [],
          "validated": true
        }
        """;

        AccountLines lines = JsonSerializer.Deserialize<AccountLines>(json, XrplJsonOptions.Default);

        Assert.IsNotNull(lines);
        Assert.AreEqual(true, lines.Validated, "validated arrives on every account_lines answer");
        CollectionAssert.DoesNotContain(
            Captured(lines.UnknownFields).ToList(),
            "validated",
            "a declared property that still shows up in the capture is not declared under the name the node uses");
    }

    /// <summary>
    /// A <c>ledger</c> call naming no ledger gets back two whole structures, neither of which is
    /// the one <c>LedgerEntity</c> holds.
    /// </summary>
    /// <remarks>
    /// The trap in this one is the word: <c>BaseLedgerEntity.Closed</c> already existed, but that
    /// is the boolean <b>inside</b> a ledger saying whether that ledger is closed - not the
    /// top-level structure of the same name.
    /// </remarks>
    [TestMethod]
    public void TestULedgerDeclaresClosedAndOpen()
    {
        const string json = """
        {
          "closed": { "ledger": { "account_hash": "9CC6B8ACCE49F5EF3213E4E051255389F3B08077BA6B4D2C4A6C6C2C6D3A1E5F", "closed": true, "ledger_index": "14" } },
          "open": { "ledger": { "closed": false, "ledger_index": "15", "parent_hash": "7953B1E3AC1B6A94A2A0C0A0C1B4D5E6F70819202A3B4C5D6E7F8091A2B3C4D5" } }
        }
        """;

        LOLedger ledger = JsonSerializer.Deserialize<LOLedger>(json, XrplJsonOptions.Default);

        Assert.IsNotNull(ledger);
        Assert.IsNotNull(ledger.ClosedLedger, "a ledger call naming nothing answers with a closed ledger");
        Assert.IsNotNull(ledger.OpenLedger, "and with the open one");

        Assert.AreEqual(true, (ledger.ClosedLedger.LedgerEntity as BaseLedgerEntity)?.Closed);
        Assert.AreEqual(false, (ledger.OpenLedger.LedgerEntity as BaseLedgerEntity)?.Closed);

        List<string> captured = Captured(ledger.UnknownFields).ToList();
        CollectionAssert.DoesNotContain(captured, "closed");
        CollectionAssert.DoesNotContain(captured, "open");
    }

    /// <summary>
    /// <c>Escrow.Flags</c> arrives even though no <c>lsfEscrow*</c> flag is defined.
    /// </summary>
    /// <remarks>
    /// The old comment reasoned that a field which is always zero need not be modelled. That
    /// confuses "always zero" with "never sent": it shows up on every deleted Escrow node in
    /// transaction metadata, and undeclared it went to the capture untyped.
    /// </remarks>
    [TestMethod]
    public void TestUEscrowDeclaresFlags()
    {
        const string json = """
        {
          "Account": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
          "Destination": "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
          "Amount": "10000",
          "Flags": 0,
          "Sequence": 3
        }
        """;

        LOEscrow escrow = JsonSerializer.Deserialize<LOEscrow>(json, XrplJsonOptions.Default);

        Assert.IsNotNull(escrow);
        Assert.AreEqual(0u, escrow.Flags, "the field arrives; being zero is not being absent");
        CollectionAssert.DoesNotContain(Captured(escrow.UnknownFields).ToList(), "Flags");
    }
}
