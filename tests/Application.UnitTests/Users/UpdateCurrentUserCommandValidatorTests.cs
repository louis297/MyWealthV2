using MyWealthV2.Application.Users.Commands.UpdateCurrentUser;
using NUnit.Framework;
using Shouldly;

namespace MyWealthV2.Application.UnitTests.Users;

public class UpdateCurrentUserCommandValidatorTests
{
    private readonly UpdateCurrentUserCommandValidator _validator = new();

    [Test]
    public void NameOnly_IsValid()
    {
        var result = _validator.Validate(new UpdateCurrentUserCommand
        {
            Name = "Ada Lovelace",
            RowVersion = "AAAA"
        });

        result.IsValid.ShouldBeTrue();
    }

    [Test]
    public void Email_IsRejected()
    {
        var result = _validator.Validate(new UpdateCurrentUserCommand
        {
            Name = "Ada",
            RowVersion = "AAAA",
            Email = "other@localhost"
        });

        result.IsValid.ShouldBeFalse();
    }

    [Test]
    public void BlankName_IsRejected()
    {
        var result = _validator.Validate(new UpdateCurrentUserCommand
        {
            Name = " ",
            RowVersion = "AAAA"
        });

        result.IsValid.ShouldBeFalse();
    }
}
