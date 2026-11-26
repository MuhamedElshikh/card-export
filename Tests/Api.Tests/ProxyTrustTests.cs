using System.Net;

namespace IdCardReader.Api.Tests;

public sealed class ProxyTrustTests
{
    [Theory]
    [InlineData("172.16.0.0/12")]
    [InlineData("192.168.0.0/16")]
    [InlineData("10.0.0.0/8")]
    [InlineData("127.0.0.1")]
    [InlineData("fd00::/8")]
    [InlineData("  10.1.2.3/32  ")]
    public void TryParseNetwork_AcceptsValidNetworks(string value)
    {
        Assert.True(ProxyTrust.TryParseNetwork(value, out var network));
        Assert.NotNull(network);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/33")]
    [InlineData("10.0.0.0/-1")]
    [InlineData("10.0.0.0/abc")]
    [InlineData("10.0.0.0/8/8")]
    [InlineData("fd00::/129")]
    public void TryParseNetwork_RejectsInvalidNetworks(string? value)
    {
        Assert.False(ProxyTrust.TryParseNetwork(value, out _));
    }

    [Fact]
    public void TryParseNetwork_ContainsTheAddressesOfTheNetwork()
    {
        Assert.True(ProxyTrust.TryParseNetwork("172.16.0.0/12", out var network));

        Assert.True(network.Contains(IPAddress.Parse("172.18.0.5")));
        Assert.False(network.Contains(IPAddress.Parse("172.32.0.1")));
        Assert.False(network.Contains(IPAddress.Parse("8.8.8.8")));
    }
}
