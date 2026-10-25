// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/shamap.test.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Utils.Hashes.ShaMap;

namespace Xrpl.Tests
{
    [TestClass]
    public class TestUSHAMap
    {
        private const string HexZero = "0000000000000000000000000000000000000000000000000000000000000000";

        private static string IntToVuc(int value)
        {
            string hex = value.ToString("X");
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                builder.Append('0').Append(hex);
            }
            return builder.ToString();
        }

        /// <summary>
        /// The xrpl.js vector: each insert lands in an empty slot at least once, which threw
        /// KeyNotFoundException before, as did hashing a node with any empty slot.
        /// </summary>
        [TestMethod]
        public void AddItem_MatchesTheXrplJsHashes()
        {
            string[] keys =
            {
                "b92891fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "b92881fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "b92691fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "b92791fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "b91891fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "b99891fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "f22891fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
                "292891fe4ef6cee585fdc6fda1e09eb4d386363158ec3321b8123e5a772c6ca8",
            };
            string[] hashes =
            {
                "B7387CFEA0465759ADC718E8C42B52D2309D179B326E239EB5075C64B6281F7F",
                "FBC195A9592A54AB44010274163CB6BA95F497EC5BA0A8831845467FB2ECE266",
                "4E7D2684B65DFD48937FFB775E20175C43AF0C94066F7D5679F51AE756795B75",
                "7A2F312EB203695FFD164E038E281839EEF06A1B99BFC263F3CECC6C74F93E07",
                "395A6691A372387A703FB0F2C6D2C405DAF307D0817F8F0E207596462B0E3A3E",
                "D044C0A696DE3169CC70AE216A1564D69DE96582865796142CE7D98A84D9DDE4",
                "76DCC77C4027309B5A91AD164083264D70B77B5E43E08AEDA5EBF94361143615",
                "DF4220E93ADC6F5569063A01B4DC79F8DB9553B6A3222ADE23DEA02BBE7230E5",
            };

            SHAMap map = new SHAMap();
            Assert.AreEqual(HexZero, map.Hash);

            for (int i = 0; i < keys.Length; i++)
            {
                map.AddItem(keys[i].ToUpperInvariant(), IntToVuc(i), NodeType.TRANSACTION_NO_METADATA);
                Assert.AreEqual(hashes[i], map.Hash, $"after key {i}");
            }
        }

        [TestMethod]
        public void GetNode_EmptySlot_IsNull()
        {
            InnerNode node = new InnerNode();

            Assert.IsNull(node.GetNode(7));
        }
    }
}
