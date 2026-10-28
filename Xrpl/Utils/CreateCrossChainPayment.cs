//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/utils/createCrossChainPayment.ts
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

namespace Xrpl.Utils
{
    public static class CrossChainPayment
    {
        /// <summary>
        /// Creates a cross-chain payment transaction.
        /// </summary>
        /// <param name="payment">he initial payment transaction. If the transaction is
        /// signed, then it will need to be re-signed.There must be no more than 2
        /// memos, since one memo is used for the sidechain destination account.The
        /// destination must be the sidechain's door account.</param>
        /// <param name="destAccount"> the destination account on the sidechain.</param>
        /// <returns>A cross-chain payment transaction, where the mainchain door account
        ///is the `Destination` and the destination account on the sidechain is encoded
        ///in the memos.</returns>
        /// <exception cref="XrplException">if there are more than 2 memos.</exception>
        public static Payment CreateCrossChainPayment(this Payment payment, string destAccount) //todo check it
        {
            var destAccountHex = destAccount.ConvertStringToHex();
            var destAccountMemo = new Memo { MemoData = destAccountHex };

            var memos = payment.Memos?.Select(c => c.Memo).ToList() ?? new List<Memo>();
            if (memos.Count > 2)
            {
                throw new XrplException("Cannot have more than 2 memos in a cross-chain transaction.");
            }
            var newMemos = new List<Memo> { destAccountMemo };
            newMemos.AddRange(memos);

            // Round-trip through JSON so every field of the source (SourceTag, Ticket, NetworkID, ...)
            // survives; a hand-written field list silently dropped whatever it forgot.
            Payment newPayment = JsonSerializer.Deserialize<Payment>(
                JsonSerializer.Serialize(payment, XrplJsonOptions.Default), XrplJsonOptions.Default);
            newPayment.Memos = newMemos.Select(c => new MemoWrapper() { Memo = c }).ToList();
            newPayment.TransactionSignature = null;

            return newPayment;
        }
    }
}
