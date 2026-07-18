using System;
using System.Threading;
using System.Threading.Tasks;

using GenerateEnums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.GenerateEnums;

[TestClass]
public class TestUServerDefinitionsClient
{
    [TestMethod]
    public async Task TestUFetch_InvalidUrl_Throws()
    {
        ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => ServerDefinitionsClient.FetchAsync("not-a-url", TimeSpan.FromSeconds(1), CancellationToken.None));
        StringAssert.Contains(ex.Message, "Invalid URL");
    }

    [TestMethod]
    public async Task TestUFetch_UnsupportedScheme_Throws()
    {
        ArgumentException ex = await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => ServerDefinitionsClient.FetchAsync("ftp://example.com/x", TimeSpan.FromSeconds(1), CancellationToken.None));
        StringAssert.Contains(ex.Message, "Unsupported URL scheme");
    }
}
