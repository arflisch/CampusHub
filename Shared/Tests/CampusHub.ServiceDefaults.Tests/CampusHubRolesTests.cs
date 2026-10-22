using System.Security.Claims;
using AwesomeAssertions;
using Dev4u.CampusHub.Rights;
using Xunit;

namespace Dev4u.CampusHub.ServiceDefaults.Tests;

public class CampusHubRolesTests
{
    private static Claim[] Token(string sub, string scope = "openid profile access.application") =>
        [new("sub", sub), new("scope", scope)];

    [Fact]
    public void A_signed_in_user_is_only_a_user()
    {
        var roles = CampusHubRoles.RolesOf(Token("u1"), new RoleOptions());

        roles.Should().Equal(CampusHubRoles.User);
        CampusHubRoles.OperationsOf(roles).Should().Equal(Access.AccessApplication);
    }

    [Fact]
    public void A_token_without_claims_gets_no_role_at_all()
    {
        CampusHubRoles.RolesOf([], new RoleOptions { Teachers = "*", Admins = "*" }).Should().BeEmpty();
    }

    [Fact]
    public void The_identity_provider_grants_roles_through_permissions()
    {
        var roles = CampusHubRoles.RolesOf(Token("u1", "access.application campushub:teach"), new RoleOptions());

        roles.Should().Equal(CampusHubRoles.User, CampusHubRoles.Teacher);
        CampusHubRoles.OperationsOf(roles).Should().Equal(Access.AccessApplication, Access.CanTeach);
    }

    [Fact]
    public void Configured_user_ids_get_their_roles()
    {
        var options = new RoleOptions { Teachers = "u2, u1", Admins = "u3" };

        CampusHubRoles.RolesOf(Token("u1"), options).Should().Equal(CampusHubRoles.User, CampusHubRoles.Teacher);
        CampusHubRoles.OperationsOf(CampusHubRoles.RolesOf(Token("u3"), options))
                      .Should().Equal(Access.AccessApplication, Access.CanSeeSwaggerFrontendApi, Access.CanSeeJobs);
        CampusHubRoles.RolesOf(Token("u11"), options).Should().Equal([CampusHubRoles.User], "ids are matched exactly, not as prefixes");
    }

    [Fact]
    public void A_star_makes_every_signed_in_user_a_teacher()
    {
        CampusHubRoles.RolesOf(Token("anyone"), new RoleOptions { Teachers = "*" })
                      .Should().Equal(CampusHubRoles.User, CampusHubRoles.Teacher);
    }
}
