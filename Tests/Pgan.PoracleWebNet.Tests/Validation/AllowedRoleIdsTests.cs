using Pgan.PoracleWebNet.Api.Controllers;

namespace Pgan.PoracleWebNet.Tests.Validation;

public class AllowedRoleIdsTests
{
    [Fact]
    public void ParsesBareCommaSeparatedIds()
    {
        var (roleIds, invalid) = AuthController.ParseAllowedRoleIds("123456789,987654321");

        Assert.Equal(["123456789", "987654321"], roleIds.OrderBy(r => r));
        Assert.Empty(invalid);
    }

    [Fact]
    public void TrimsWhitespaceAroundIds()
    {
        var (roleIds, invalid) = AuthController.ParseAllowedRoleIds(" 123456789 , 987654321 ");

        Assert.Equal(["123456789", "987654321"], roleIds.OrderBy(r => r));
        Assert.Empty(invalid);
    }

    // The setting's example was rendered with quotes, so admins pasted them in and every
    // non-admin login was denied. See #367.
    [Theory]
    [InlineData("\"123456789,987654321\"")]
    [InlineData("'123456789,987654321'")]
    [InlineData("“123456789,987654321”")]
    [InlineData("«123456789,987654321»")]
    [InlineData("„123456789,987654321“")]
    [InlineData("\"123456789\",\"987654321\"")]
    public void StripsQuotesFromPastedValues(string value)
    {
        var (roleIds, invalid) = AuthController.ParseAllowedRoleIds(value);

        Assert.Equal(["123456789", "987654321"], roleIds.OrderBy(r => r));
        Assert.Empty(invalid);
    }

    [Fact]
    public void ReportsNonSnowflakeEntriesAsInvalid()
    {
        var (roleIds, invalid) = AuthController.ParseAllowedRoleIds("123456789,@Trainers,987654321");

        Assert.Equal(["123456789", "987654321"], roleIds.OrderBy(r => r));
        Assert.Equal(["@Trainers"], invalid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    [InlineData("\"\"")]
    public void YieldsNoRoleIdsForEmptyValues(string? value)
    {
        var (roleIds, invalid) = AuthController.ParseAllowedRoleIds(value);

        Assert.Empty(roleIds);
        Assert.Empty(invalid);
    }

    [Fact]
    public void DeduplicatesRepeatedIds()
    {
        var (roleIds, _) = AuthController.ParseAllowedRoleIds("123456789,123456789");

        Assert.Equal(["123456789"], roleIds);
    }

    // The gate is an allow-list: one matching role is enough. It used to require every listed
    // role (IsSubsetOf), which locked out anyone holding only some of them. See #367.
    [Fact]
    public void GrantsAccessWhenUserHasOneOfSeveralAllowedRoles()
    {
        var allowed = new HashSet<string> { "123456789", "987654321" };
        var userRoles = new HashSet<string> { "987654321", "555" };

        Assert.True(AuthController.HasAllowedRole(allowed, userRoles));
    }

    [Fact]
    public void GrantsAccessWhenUserHasEveryAllowedRole()
    {
        var allowed = new HashSet<string> { "123456789", "987654321" };
        var userRoles = new HashSet<string> { "123456789", "987654321" };

        Assert.True(AuthController.HasAllowedRole(allowed, userRoles));
    }

    [Fact]
    public void DeniesAccessWhenUserHasNoneOfTheAllowedRoles()
    {
        var allowed = new HashSet<string> { "123456789", "987654321" };
        var userRoles = new HashSet<string> { "555", "666" };

        Assert.False(AuthController.HasAllowedRole(allowed, userRoles));
    }

    [Fact]
    public void DeniesAccessWhenUserHasNoRoles()
    {
        var allowed = new HashSet<string> { "123456789" };

        Assert.False(AuthController.HasAllowedRole(allowed, []));
    }
}
