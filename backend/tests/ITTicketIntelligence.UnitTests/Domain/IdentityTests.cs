using ITTicketIntelligence.Domain.Common;
using ITTicketIntelligence.Domain.Enums;
using ITTicketIntelligence.Domain.Identity;
using ITTicketIntelligence.Domain.ValueObjects;
using Xunit;

namespace ITTicketIntelligence.UnitTests.Domain;

public class IdentityTests
{
    [Fact]
    public void UserProfileAndRole_CanBeChangedWithoutChangingIdentity()
    {
        var user = NewUser();
        var id = user.Id;

        user.UpdateProfile(" Sara Benali ", EmailAddress.Create("SARA@example.com"), DomainTestData.Now.AddMinutes(1));
        user.ChangeRole(UserRole.Agent, DomainTestData.Now.AddMinutes(2));

        Assert.Equal(id, user.Id);
        Assert.Equal("Sara Benali", user.Name);
        Assert.Equal("sara@example.com", user.Email.Value);
        Assert.Equal(UserRole.Agent, user.Role);
    }

    [Fact]
    public void DeactivatingAUser_PreservesItsIdentityAndAllowsReactivation()
    {
        var user = NewUser();
        var id = user.Id;
        user.Deactivate(DomainTestData.Now.AddMinutes(1));
        Assert.False(user.IsActive);
        Assert.Equal(id, user.Id);

        user.Reactivate(DomainTestData.Now.AddMinutes(2));
        Assert.True(user.IsActive);
    }

    [Fact]
    public void User_RejectsInvalidIdentityProfileRoleAndTime()
    {
        Assert.Throws<DomainException>(() => new User(Guid.Empty, "Sara", EmailAddress.Create("sara@example.com"), UserRole.User, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new User(Guid.NewGuid(), " ", EmailAddress.Create("sara@example.com"), UserRole.User, DomainTestData.Now));
        Assert.Throws<ArgumentNullException>(() => new User(Guid.NewGuid(), "Sara", null!, UserRole.User, DomainTestData.Now));
        Assert.Throws<DomainException>(() => new User(Guid.NewGuid(), "Sara", EmailAddress.Create("sara@example.com"), (UserRole)999, DomainTestData.Now));
        var user = NewUser();
        Assert.Throws<DomainException>(() => user.ChangeRole((UserRole)999, DomainTestData.Now));
        Assert.Throws<DomainException>(() => user.UpdateProfile("Other name", EmailAddress.Create("other@example.com"), DomainTestData.Now.AddMinutes(-1)));
        Assert.Equal("Sara", user.Name);
        Assert.Equal(UserRole.User, user.Role);
    }

    [Fact]
    public void TeamMembership_IsUniqueAndCannotBeMutatedThroughTheCollection()
    {
        var team = new Team(Guid.NewGuid(), "Database Team", DomainTestData.Now);
        var userId = Guid.NewGuid();

        team.AddMember(userId, DomainTestData.Now);

        Assert.Throws<DomainException>(() => team.AddMember(userId, DomainTestData.Now));
        Assert.Throws<DomainException>(() => team.AddMember(Guid.Empty, DomainTestData.Now));
        Assert.Throws<NotSupportedException>(() => ((ICollection<Guid>)team.MemberIds).Add(Guid.NewGuid()));
        Assert.Single(team.MemberIds);

        team.RemoveMember(userId, DomainTestData.Now.AddMinutes(1));
        Assert.Empty(team.MemberIds);
        Assert.Throws<DomainException>(() => team.RemoveMember(userId, DomainTestData.Now.AddMinutes(1)));
    }

    [Fact]
    public void InactiveTeam_RejectsNewMembersButKeepsExistingMemberships()
    {
        var team = new Team(Guid.NewGuid(), "Database Team", DomainTestData.Now);
        var userId = Guid.NewGuid();
        team.AddMember(userId, DomainTestData.Now);
        team.Deactivate(DomainTestData.Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => team.AddMember(Guid.NewGuid(), DomainTestData.Now.AddMinutes(2)));
        Assert.Contains(userId, team.MemberIds);

        team.Reactivate(DomainTestData.Now.AddMinutes(3));
        team.AddMember(Guid.NewGuid(), DomainTestData.Now.AddMinutes(4));
        Assert.Equal(2, team.MemberIds.Count);
    }

    [Fact]
    public void BackdatedMembershipChange_DoesNotAlterTheTeam()
    {
        var team = new Team(Guid.NewGuid(), "Database Team", DomainTestData.Now);
        var userId = Guid.NewGuid();
        team.AddMember(userId, DomainTestData.Now.AddMinutes(2));

        Assert.Throws<DomainException>(() => team.RemoveMember(userId, DomainTestData.Now.AddMinutes(1)));
        Assert.Contains(userId, team.MemberIds);
    }

    [Fact]
    public void TeamDetails_ValidateBeforeChangingAnyField()
    {
        var team = new Team(Guid.NewGuid(), "Database Team", DomainTestData.Now);
        Assert.Null(team.Description);
        Assert.Throws<DomainException>(() => team.UpdateDetails("Other team", new string('x', 1001), DomainTestData.Now));
        Assert.Equal("Database Team", team.Name);

        team.UpdateDetails(" Database Support ", " Internal support ", DomainTestData.Now.AddMinutes(1));
        Assert.Equal("Database Support", team.Name);
        Assert.Equal("Internal support", team.Description);
    }

    private static User NewUser() => new(Guid.NewGuid(), "Sara", EmailAddress.Create("sara@example.com"), UserRole.User, DomainTestData.Now);
}
