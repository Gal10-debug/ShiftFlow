using System.Text.Json;
using ShiftManager.Development;
using ShiftManager.Services;
namespace ShiftManager.Tests;

public sealed class DemoScenarioTests
{
    [Fact]
    public void Plan_is_repeatable_distinct_balanced_and_excludes_leaders()
    {
        var month = new DateOnly(2026, 12, 1);
        var accounts = DemoPlan.Accounts(42).Where(a => a.Position != "leader").ToArray();
        var plan = DemoScenario.Plan(month, 42, accounts);
        Assert.Equal(JsonSerializer.Serialize(plan), JsonSerializer.Serialize(DemoScenario.Plan(month, 42, accounts.Reverse())));
        Assert.Equal(33, plan.Select(c => c.Account.Id).Distinct().Count());
        Assert.Equal(new[] { 4, 5, 8, 6, 10 }, new[] { "RESERVE", "VACATION", "STUDIES", "RECURRING_OTHER", "SHORT" }.Select(k => plan.Count(c => c.Category == k)));
        Assert.DoesNotContain(plan, c => c.Account.Position == "leader");
        Assert.All(plan.GroupBy(c => c.Account.Team), team => Assert.InRange(team.Count(), 8, 9));
        Assert.Contains(plan, c => c.Account.Qualified);
        Assert.Contains(plan, c => !c.Account.Qualified);
        Assert.Contains(plan, c => c.Workflow == "MODIFY");
        Assert.Contains(plan, c => c.Workflow == "CANCEL");
        Assert.Contains(plan, c => c.Workflow == "PENDING");
        Assert.Contains(plan, c => c.Workflow == "REJECTED");
        Assert.Equal(30, plan[0].Pattern.EndDate!.Value.DayNumber - plan[0].Pattern.StartDate.DayNumber + 1);
        Assert.Equal(14, plan[1].Pattern.EndDate!.Value.DayNumber - plan[1].Pattern.StartDate.DayNumber + 1);
        Assert.Equal(7, plan[2].Pattern.EndDate!.Value.DayNumber - plan[2].Pattern.StartDate.DayNumber + 1);
        Assert.NotEqual(plan[3].Pattern.StartDate.Month, plan[3].Pattern.EndDate!.Value.Month);
        foreach (var c in plan.Where(c => c.Pattern.Recurring))
        {
            var p = c.Pattern;
            var rule = new AbsenceRule(Guid.Empty, c.Account.Id, p.StartDate, p.EndDate, true, p.Weekdays, p.FullDay, p.StartTime, p.EndTime);
            foreach (var m in new[] { month.AddMonths(-1), month })
            {
                var day = Enumerable.Range(0, 7).Select(m.AddDays).First(d => p.Weekdays.Contains((int)d.DayOfWeek));
                Assert.True(AbsenceRules.Overlaps(rule, AbsenceRules.Utc(day.ToDateTime(new(0, 0)), false), AbsenceRules.Utc(day.AddDays(1).ToDateTime(new(0, 0)), true)));
            }
        }
    }
    [Fact]
    public void Too_few_eligible_employees_refuses_instead_of_selecting_a_leader() =>
        Assert.Throws<InvalidOperationException>(() => DemoScenario.Plan(new(2026, 12, 1), 42, DemoPlan.Accounts(42).Take(20)));
}
