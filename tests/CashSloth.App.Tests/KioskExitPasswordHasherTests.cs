using CashSloth.App;
using Xunit;

namespace CashSloth.App.Tests;

public sealed class KioskExitPasswordHasherTests
{
    [Fact]
    public void VerifiesMatchingPassword()
    {
        var hash = KioskExitPasswordHasher.Hash("1234");

        Assert.True(KioskExitPasswordHasher.Verify("1234", hash));
    }

    [Fact]
    public void RejectsWrongOrMalformedPassword()
    {
        var hash = KioskExitPasswordHasher.Hash("1234");

        Assert.False(KioskExitPasswordHasher.Verify("9999", hash));
        Assert.False(KioskExitPasswordHasher.Verify("1234", "not-a-supported-hash"));
    }
}
