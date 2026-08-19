using CashSloth.Contracts;
using CashSloth.Server.Api;
using CashSloth.Server.Data;
using CashSloth.Server.Services;
using Microsoft.AspNetCore.Identity;

namespace CashSloth.Server.Tests;

public sealed class AccountPolicyTests
{
    [Fact]
    public async Task FirstAdmin_RequiresStrongPassword_AndHasNoDefaultCredentials()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();

        var weak = await Assert.ThrowsAsync<ApiProblemException>(() =>
            accounts.CreateFirstAdminAsync("admin", "admin"));
        Assert.Equal("setup_failed", weak.Code);

        await accounts.CreateFirstAdminAsync("owner", "Very-Strong-Password-42!");
        var listed = await accounts.ListAccountsAsync();
        var owner = Assert.Single(listed);
        Assert.Equal(CashSlothRoles.Admin, owner.Role);
        Assert.True(owner.IsApproved);
    }

    [Fact]
    public async Task LastActiveAdmin_CannotBeBlockedOrDemoted()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateFirstAdminAsync("owner", "Very-Strong-Password-42!");
        var owner = Assert.Single(await accounts.ListAccountsAsync());

        var block = await Assert.ThrowsAsync<ApiProblemException>(() =>
            accounts.SetActiveAsync(owner.Id, false, "test"));
        Assert.Equal("last_active_admin", block.Code);

        var demote = await Assert.ThrowsAsync<ApiProblemException>(() =>
            accounts.SetRoleAsync(owner.Id, CashSlothRoles.User, "test"));
        Assert.Equal("last_active_admin", demote.Code);
    }

    [Fact]
    public async Task PasswordReset_ProducesTemporaryPasswordAndForcesChange()
    {
        await using var context = await TestServerContext.CreateAsync();
        using var scope = context.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.CreateFirstAdminAsync("owner", "Very-Strong-Password-42!");
        var owner = Assert.Single(await accounts.ListAccountsAsync());

        var temporary = await accounts.ResetPasswordAsync(owner.Id, "test");
        Assert.True(temporary.Length >= 12);
        var updated = Assert.Single(await accounts.ListAccountsAsync());
        Assert.True(updated.MustChangePassword);
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ServerUser>>();
        var user = await userManager.FindByIdAsync(owner.Id);
        Assert.True(await userManager.CheckPasswordAsync(user!, temporary));
    }
}
