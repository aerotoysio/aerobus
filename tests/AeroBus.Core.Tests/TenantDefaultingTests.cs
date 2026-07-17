using System.Security.Claims;
using AeroBus.Core.Model.Catalogue;
using AeroBus.Core.Security;
using Xunit;

namespace AeroBus.Core.Tests;

/// <summary>
/// Regression coverage for catalogue documents saved without a companyId:
/// they used to persist with CompanyId = Guid.Empty, invisible to every
/// tenant-scoped query (e.g. /operations/departures). The save endpoints now
/// run the posted id through ResolveCompanyId, which falls back to the
/// caller's companyId claim.
/// </summary>
public class TenantDefaultingTests
{
    private static ClaimsPrincipal UserWithCompany(Guid companyId) =>
        new(new ClaimsIdentity([new Claim("companyId", companyId.ToString())], "test"));

    [Fact]
    public void Missing_company_defaults_to_the_callers_claim()
    {
        var company = Guid.NewGuid();
        Assert.Equal(company, UserWithCompany(company).ResolveCompanyId(null));
    }

    [Fact]
    public void Empty_company_defaults_to_the_callers_claim()
    {
        var company = Guid.NewGuid();
        Assert.Equal(company, UserWithCompany(company).ResolveCompanyId(Guid.Empty));
    }

    [Fact]
    public void Posted_company_wins_over_the_claim()
    {
        var posted = Guid.NewGuid();
        Assert.Equal(posted, UserWithCompany(Guid.NewGuid()).ResolveCompanyId(posted));
    }

    [Fact]
    public void Schedule_posted_without_company_takes_the_callers_company()
    {
        // A JSON body without companyId binds Schedule.CompanyId (non-nullable
        // Guid) to Guid.Empty — the exact shape that produced orphaned flights.
        var company = Guid.NewGuid();
        var user = UserWithCompany(company);
        var schedule = new Schedule
        {
            Id = Guid.NewGuid(),
            CarrierCode = "AT",
            FlightNumber = "123",
            DepartureStation = "AMS",
            ArrivalStation = "LIS",
        };

        schedule = schedule with { CompanyId = user.ResolveCompanyId(schedule.CompanyId) };

        Assert.Equal(company, schedule.CompanyId);
    }

    [Fact]
    public void Caller_without_claim_and_no_posted_company_yields_empty()
    {
        // Documents current behavior: nothing to default from. Endpoints sit
        // behind auth, so a real caller always carries the claim.
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Equal(Guid.Empty, user.ResolveCompanyId(null));
    }
}
