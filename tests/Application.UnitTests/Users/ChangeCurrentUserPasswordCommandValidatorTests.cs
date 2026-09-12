using MyWealthV2.Application.Users.Commands.ChangeCurrentUserPassword;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Users;

public class ChangeCurrentUserPasswordCommandValidatorTests
{
    private readonly ChangeCurrentUserPasswordCommandValidator _validator = new();

    [Test]
    public void ValidPasswords_AreAccepted()
    {
        var result = _validator.Validate(new ChangeCurrentUserPasswordCommand
        {
            CurrentPassword = "OldPassword1",
            NewPassword = "NewPassword1"
        });

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void ShortNewPassword_IsRejected()
    {
        var result = _validator.Validate(new ChangeCurrentUserPasswordCommand
        {
            CurrentPassword = "OldPassword1",
            NewPassword = "short"
        });

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void BlankCurrentPassword_IsRejected()
    {
        var result = _validator.Validate(new ChangeCurrentUserPasswordCommand
        {
            CurrentPassword = " ",
            NewPassword = "NewPassword1"
        });

        result.IsValid.ShouldBeFalse();
    }
}
