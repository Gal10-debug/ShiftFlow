using System.Text.Json;
using ShiftManager.Development;
using ShiftManager.Services;
namespace ShiftManager.Tests;

public sealed class DemoPlanTests
{
    [Fact]
    public void Accounts_are_deterministic_with_four_balanced_teams_and_independent_qualifications()
    {
        var accounts = DemoPlan.Accounts(42);
        Assert.Equal(accounts, DemoPlan.Accounts(42));
        Assert.NotEqual(accounts[0].Id, DemoPlan.Accounts(43)[0].Id);
        Assert.Equal(60, accounts.Select(a => a.Id).Distinct().Count());
        Assert.Equal(60, accounts.Select(a => a.Login).Distinct().Count());
        Assert.Equal(4, accounts.Count(a => a.Position == "leader"));
        Assert.Equal(4, accounts.Count(a => a.Position == "deputy"));
        Assert.Equal(52, accounts.Count(a => a.Position == "employee"));
        Assert.Equal(4, accounts.GroupBy(a => a.Team).Count());
        foreach (var team in accounts.GroupBy(a => a.Team))
        {
            Assert.Equal(15, team.Count());
            Assert.Single(team, a => a.Position == "leader");
            Assert.Single(team, a => a.Position == "deputy");
        }
        Assert.Contains(accounts, a => a.Position == "employee" && a.Qualified);
        Assert.Contains(accounts, a => a.Position == "deputy" && !a.Qualified);
    }
    [Theory]
    [InlineData("Production", "localhost", "shiftflow_demo_202611_42")]
    [InlineData("Staging", "localhost", "shiftflow_demo_202611_42")]
    [InlineData("Development", "db.example.com", "shiftflow_demo_202611_42")]
    [InlineData("Testing", "localhost", "shiftmanager")]
    [InlineData("Development", "localhost", "shiftflow_demo_202610_42")]
    public void Unverified_targets_are_rejected(string environment, string host, string database) =>
        Assert.Throws<InvalidOperationException>(() => DemoCli.ValidateTarget(environment, $"Host={host};Database={database}", new(2026,11,1),42));
    [Fact]
    public void Adjacent_months_share_valid_sunday_weekly_block_budgets()
    {
        foreach (var seed in new[] {42, -1, int.MaxValue})
        foreach (var index in Enumerable.Range(0,60))
        {
            var dates = new[] { new DateOnly(2026,10,1), new DateOnly(2026,11,1), new DateOnly(2026,12,1) };
            var choices = dates.SelectMany(m => DemoPlan.Selections(m,seed,index)).ToArray();
            Assert.All(choices.GroupBy(c => AvailabilityService.WeekStart(c.Date)), g => Assert.InRange(g.Count(c=>c.State=="BLOCKED"),0,3));
            Assert.Equal(choices.Length, choices.DistinctBy(c=>(c.Date,c.ShiftType)).Count());
            Assert.Equal(JsonSerializer.Serialize(choices), JsonSerializer.Serialize(dates.SelectMany(m=>DemoPlan.Selections(m,seed,index))));
        }
        Assert.NotEqual(JsonSerializer.Serialize(DemoPlan.Selections(new(2026,11,1),42,3)),JsonSerializer.Serialize(DemoPlan.Selections(new(2026,11,1),43,3)));
    }
}
