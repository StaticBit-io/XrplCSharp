

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/ticketCreate.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUTicketCreate
    {
        public static Dictionary<string, object> ticketCreate;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            ticketCreate = new Dictionary<string, object>
            {
                {"TransactionType", "TicketCreate"},
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"TicketCount", 150u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            //verifies valid TicketCreate
            Validation.ValidateTicketCreate(ticketCreate);
            Validation.Validate(ticketCreate);

            // throws when TicketCount is missing
            ticketCreate.Remove("TicketCount");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTicketCreate(ticketCreate), "TicketCreate: missing field TicketCount");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(ticketCreate), "TicketCreate: missing field TicketCount");
            ticketCreate["TicketCount"] = 150u;

            // throws when TicketCount is not a number
            ticketCreate["TicketCount"] = "150";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTicketCreate(ticketCreate), "TicketCreate: TicketCount must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(ticketCreate), "TicketCreate: TicketCount must be a number");
            ticketCreate["TicketCount"] = 150u;

            // throws when TicketCount is not an uint
            ticketCreate["TicketCount"] = 12.5;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTicketCreate(ticketCreate), "TicketCreate: TicketCount must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(ticketCreate), "TicketCreate: TicketCount must be a number");
            ticketCreate["TicketCount"] = 150u;

            // throws when TicketCount is < 1
            ticketCreate["TicketCount"] = 0u;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTicketCreate(ticketCreate), "TicketCreate: TicketCount must be an integer from 1 to 250");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(ticketCreate), "TicketCreate: TicketCount must be an integer from 1 to 250");
            ticketCreate["TicketCount"] = 150u;

            // throws when TicketCount is > 250
            ticketCreate["TicketCount"] = 251u;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTicketCreate(ticketCreate), "TicketCreate: TicketCount must be an integer from 1 to 250");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(ticketCreate), "TicketCreate: TicketCount must be an integer from 1 to 250");
            ticketCreate["TicketCount"] = 150u;
        }
    }

}

