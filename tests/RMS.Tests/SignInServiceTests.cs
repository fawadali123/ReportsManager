using RMS.Core.Abstractions;
using RMS.Core.Domain;
using RMS.Core.Services;
using RMS.Tests.Fakes;

namespace RMS.Tests;

public class SignInServiceTests
{
    private readonly InMemoryStore _store = new();
    private readonly FakeDirectory _directory = new();
    private readonly SignInService _service;

    public SignInServiceTests()
    {
        _service = new SignInService(_directory, _store, new AccessOptions());
        _store.Users.Add(new AppUser { Id = 1, Code = "U001", AdUsername = "fawad", Role = Roles.Admin, Active = true });
        _store.Users.Add(new AppUser { Id = 2, Code = "U002", AdUsername = "sara", Role = Roles.Viewer, Active = true });
        _store.Users.Add(new AppUser { Id = 3, Code = "U003", AdUsername = "omar", Role = Roles.Viewer, Active = false });
    }

    private void DirectoryAccepts(string account, params string[] groups) =>
        _directory.Result = new DirectoryAuthResult(DirectoryAuthStatus.Success, account, MemberOf: groups);

    [Theory]
    [InlineData("Admin", true, true, "Admin")]
    [InlineData("Admin", true, false, "Admin")]
    [InlineData("Admin", false, true, "Viewer")]    // marked Admin in the table but not in RMS_Admin
    [InlineData("Viewer", true, false, "Viewer")]   // in RMS_Admin but not marked Admin in the table
    [InlineData("Viewer", false, true, "Viewer")]
    [InlineData("Admin", false, false, null)]
    [InlineData("Viewer", false, false, null)]
    public void Role_needs_both_the_table_and_the_group(string databaseRole, bool inAdminGroup, bool inUsersGroup, string? expected)
    {
        Assert.Equal(expected, SignInService.ResolveRole(databaseRole, inAdminGroup, inUsersGroup));
    }

    [Theory]
    [InlineData("fawad", "fawad")]
    [InlineData("  CORP\\fawad ", "fawad")]
    [InlineData("fawad@corp.example.com", "fawad")]
    [InlineData("", "")]
    public void Usernames_are_reduced_to_the_account_name(string input, string expected)
    {
        Assert.Equal(expected, SignInService.NormalizeUsername(input));
    }

    [Fact]
    public async Task Admin_in_the_admin_group_signs_in_as_admin()
    {
        DirectoryAccepts("Fawad", "RMS_Admin");
        var result = await _service.SignInAsync("CORP\\fawad", "secret");

        Assert.True(result.Succeeded);
        Assert.Equal(Roles.Admin, result.Role);
        Assert.Equal(1, result.User!.Id);
    }

    [Fact]
    public async Task Viewer_in_the_users_group_signs_in_as_viewer()
    {
        DirectoryAccepts("sara", "RMS_Users");
        var result = await _service.SignInAsync("sara", "secret");

        Assert.True(result.Succeeded);
        Assert.Equal(Roles.Viewer, result.Role);
    }

    [Fact]
    public async Task Empty_password_is_refused_without_asking_the_directory()
    {
        DirectoryAccepts("fawad", "RMS_Admin");
        var result = await _service.SignInAsync("fawad", "");

        Assert.Equal(SignInStatus.InvalidCredentials, result.Status);
        Assert.Equal(0, _directory.Calls);
    }

    [Fact]
    public async Task Wrong_password_is_refused()
    {
        _directory.Result = new DirectoryAuthResult(DirectoryAuthStatus.InvalidCredentials);
        Assert.Equal(SignInStatus.InvalidCredentials, (await _service.SignInAsync("fawad", "nope")).Status);
    }

    [Fact]
    public async Task Valid_ad_account_that_is_not_in_the_user_table_is_refused()
    {
        DirectoryAccepts("stranger", "RMS_Admin", "RMS_Users");
        Assert.Equal(SignInStatus.NotRegistered, (await _service.SignInAsync("stranger", "secret")).Status);
    }

    [Fact]
    public async Task Inactive_user_is_refused()
    {
        DirectoryAccepts("omar", "RMS_Users");
        Assert.Equal(SignInStatus.Inactive, (await _service.SignInAsync("omar", "secret")).Status);
    }

    [Fact]
    public async Task User_in_neither_group_is_refused()
    {
        DirectoryAccepts("sara");
        Assert.Equal(SignInStatus.NotInGroup, (await _service.SignInAsync("sara", "secret")).Status);
    }

    [Fact]
    public async Task Directory_outage_is_reported_as_such()
    {
        _directory.Result = new DirectoryAuthResult(DirectoryAuthStatus.Unavailable, Error: "server down");
        var result = await _service.SignInAsync("fawad", "secret");

        Assert.Equal(SignInStatus.DirectoryUnavailable, result.Status);
        Assert.DoesNotContain("server down", result.Message);
    }
}
