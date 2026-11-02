using Lucarne.Core.Updates;

namespace Lucarne.Core.Tests.Updates;

/// <summary>Which addresses an update download may talk to.</summary>
public sealed class UpdateHostsTests
{
    [Theory]
    [InlineData("https://api.github.com/repos/dasimon/lucarne/releases/latest")]
    [InlineData("https://github.com/dasimon/lucarne/releases/download/v0.9.0/Lucarne.exe")]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1/2?x=y")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/1/2")]
    [InlineData("https://GITHUB.com/dasimon/lucarne")]
    public void The_hosts_of_the_release_flow_are_allowed(string url)
    {
        Assert.True(UpdateHosts.IsAllowed(new Uri(url)));
    }

    [Theory]
    [InlineData("http://github.com/dasimon/lucarne")]
    [InlineData("https://github.com.evil.example/dasimon/lucarne")]
    [InlineData("https://evil.example/github.com")]
    [InlineData("https://user@evil.example/")]
    [InlineData("https://evilgithub.com/")]
    [InlineData("https://raw.githubusercontent.com/dasimon/lucarne/main/README.md")]
    [InlineData("ftp://github.com/file")]
    public void Any_other_address_is_refused(string url)
    {
        Assert.False(UpdateHosts.IsAllowed(new Uri(url)));
    }

    [Fact]
    public void Nothing_and_relative_addresses_are_refused()
    {
        Assert.False(UpdateHosts.IsAllowed(null));
        Assert.False(UpdateHosts.IsAllowed(new Uri("/dasimon/lucarne", UriKind.Relative)));
    }
}
