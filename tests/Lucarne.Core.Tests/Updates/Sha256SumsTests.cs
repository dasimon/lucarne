using Lucarne.Core.Updates;

namespace Lucarne.Core.Tests.Updates;

/// <summary>Finding the expected hash of a file in a <c>SHA256SUMS</c> listing.</summary>
public sealed class Sha256SumsTests
{
    private const string Hash = "bee805430933bdf830ed5f168e2484309c285eb9ef941f8b9d780af4164c6b07";

    [Theory]
    // What release.yml produces: binary marker, no end of line.
    [InlineData(Hash + " *Lucarne.exe")]
    // Text-mode form of sha256sum.
    [InlineData(Hash + "  Lucarne.exe")]
    [InlineData(Hash + " *Lucarne.exe\n")]
    [InlineData(Hash + " *Lucarne.exe\r\n")]
    public void The_hash_of_the_file_is_found(string content)
    {
        Assert.Equal(Hash, Sha256Sums.Find(content, "Lucarne.exe"));
    }

    [Fact]
    public void An_upper_case_hash_is_returned_in_lower_case()
    {
        Assert.Equal(Hash, Sha256Sums.Find(Hash.ToUpperInvariant() + " *Lucarne.exe", "Lucarne.exe"));
    }

    [Fact]
    public void The_right_line_is_picked_among_several()
    {
        var other = new string('a', 64);
        var content = $"{other} *Lucarne.exe.bak\r\n{other} *Other.zip\r\n{Hash} *Lucarne.exe\r\n";

        Assert.Equal(Hash, Sha256Sums.Find(content, "Lucarne.exe"));
    }

    [Theory]
    // What release.yml writes since the rename: the same bytes under both names, so that a
    // RemoteDeck 0.9.0, which looks for its own name, finds a line as well as Lucarne does.
    [InlineData("Lucarne.exe")]
    [InlineData("RemoteDeck.exe")]
    public void Either_name_is_found_in_the_two_line_listing(string fileName)
    {
        var content = $"{Hash} *Lucarne.exe\n{Hash} *RemoteDeck.exe";

        Assert.Equal(Hash, Sha256Sums.Find(content, fileName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    [InlineData("not a listing at all")]
    // A name that only starts or ends the same is another file.
    [InlineData(Hash + " *Lucarne.exe.bak")]
    [InlineData(Hash + " *MyLucarne.exe")]
    // 63 characters.
    [InlineData("ee805430933bdf830ed5f168e2484309c285eb9ef941f8b9d780af4164c6b07 *Lucarne.exe")]
    // 65 characters.
    [InlineData("0" + Hash + " *Lucarne.exe")]
    // Not hexadecimal ('g').
    [InlineData("gee805430933bdf830ed5f168e2484309c285eb9ef941f8b9d780af4164c6b07 *Lucarne.exe")]
    // No separator between hash and name.
    [InlineData(Hash + "Lucarne.exe")]
    public void Anything_else_gives_nothing(string content)
    {
        Assert.Null(Sha256Sums.Find(content, "Lucarne.exe"));
    }
}
