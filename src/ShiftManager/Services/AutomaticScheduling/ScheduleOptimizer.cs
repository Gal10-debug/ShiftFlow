using System.Diagnostics;
using System.Globalization;
using Google.OrTools.Sat;
using ShiftManager.Api;
namespace ShiftManager.Services.AutomaticScheduling;

public sealed record OptimizationResult(string Status, int ProvenObjectives, GeneratedCell[] Cells);
public interface IScheduleOptimizer
{
    OptimizationResult Solve(SchedulingProblem problem, double seconds, CancellationToken ct);
}
public sealed class ScheduleOptimizer : IScheduleOptimizer
{
    public OptimizationResult Solve(SchedulingProblem p, double seconds, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var model = new CpModel();
        var choices = new Dictionary<(int Cell, int Employee), BoolVar>();
        var fixedWork = p.Work.Where(w => !w.Replaceable).ToArray();
        var fixedViolations = SchedulingRules.Violations(fixedWork, p.Year, p.Month, pairing: false);
        if (fixedViolations.Length > 0) throw new ApiException(fixedViolations[0].Code, 409);
        if (fixedWork.Any(w => w.Semantic == SchedulingRules.OnCall && !SchedulingRules.Weekend(w.Date) && w.Date.Year == p.Year && w.Date.Month == p.Month))
            throw new ApiException("ON_CALL_WEEKEND_ONLY", 409);
        foreach (var w in fixedWork.Where(w => w.Semantic == SchedulingRules.OnCall && SchedulingRules.Weekend(w.Date)))
        {
            var mate = w.Date.DayOfWeek == DayOfWeek.Friday ? w.Date.AddDays(1) : w.Date.AddDays(-1);
            if (mate.Year != p.Year || mate.Month != p.Month) continue;
            if (!fixedWork.Any(o => o.DefinitionId == w.DefinitionId && o.Date == mate && o.UserId == w.UserId)
                && (!p.Cells.Any(c => c.DefinitionId == w.DefinitionId && c.Date == mate) || !p.Employees.Any(e => e.Id == w.UserId)))
                throw new ApiException("ON_CALL_PAIR_REQUIRED", 409);
        }
        // Validate fixed assignments belonging to included cells; legacy/excluded commitments
        // remain untouched and are obstacles, never candidates to "repair" by deletion.
        foreach (var c in p.Cells)
        {
            var members = fixedWork.Where(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date).ToArray();
            if (members.Length > c.Required || members.Any(w => w.Start != c.Start || w.End != c.End || !p.Employees.Any(e => e.Id == w.UserId && !e.IsTeamLeader && (!c.RequiresShiftLeadQualification || e.IsShiftLeadQualified))
                || p.Preferences.Any(b => b.UserId == w.UserId && b.State == Domain.PreferenceType.BLOCKED && SchedulingProblem.OverlapsAnchor(c, b))
                || fixedWork.Any(o => o.AssignmentId != w.AssignmentId && o.UserId == w.UserId && SchedulingProblem.Conflicts(w.Start, w.End, o.Start, o.End))))
                throw new ApiException("PROTECTED_ASSIGNMENT_CONFLICT", 409);
        }
        for (var ci = 0; ci < p.Cells.Length; ci++)
        {
            ct.ThrowIfCancellationRequested();
            var c = p.Cells[ci];
            for (var ei = 0; ei < p.Employees.Length; ei++)
                if ((c.Semantic != SchedulingRules.OnCall || SchedulingRules.Weekend(c.Date)) && p.Eligible(p.Employees[ei], c)) choices[(ci, ei)] = model.NewBoolVar($"x{ci}_{ei}");
            var capacity = c.Required - fixedWork.Count(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date);
            model.Add(LinearExpr.Sum(choices.Where(x => x.Key.Cell == ci).Select(x => x.Value)) <= capacity);
        }
        // Equality couples Friday/Saturday choices; boundary counterparts are read-only constants.
        LinearExpr Presence(Guid definition, DateOnly date, int ei)
        {
            var ci = Array.FindIndex(p.Cells, c => c.DefinitionId == definition && c.Date == date);
            if (ci >= 0 && choices.TryGetValue((ci, ei), out var variable)) return variable;
            return LinearExpr.Constant(fixedWork.Any(w => w.DefinitionId == definition && w.Date == date && w.UserId == p.Employees[ei].Id) ? 1 : 0);
        }
        foreach (var pair in p.Cells.Where(c => c.Semantic == SchedulingRules.OnCall && SchedulingRules.Weekend(c.Date))
            .Select(c => (c.DefinitionId, Friday: SchedulingRules.Friday(c.Date))).Distinct())
            for (var ei = 0; ei < p.Employees.Length; ei++)
                model.Add(Presence(pair.DefinitionId, pair.Friday, ei) == Presence(pair.DefinitionId, pair.Friday.AddDays(1), ei));
        for (var ei = 0; ei < p.Employees.Length; ei++)
        {
            var user = p.Employees[ei].Id;
            var options = choices.Where(x => x.Key.Employee == ei).ToArray();
            var existing = fixedWork.Where(w => w.UserId == user).ToArray();
            foreach (var week in p.Cells.Select(c => SchedulingRules.Week(c.Date)).Distinct())
            {
                model.Add(LinearExpr.Sum(options.Where(x => p.Cells[x.Key.Cell].Semantic == SchedulingRules.SixNine && SchedulingRules.Week(p.Cells[x.Key.Cell].Date) == week).Select(x => x.Value))
                    + existing.Count(w => w.Semantic == SchedulingRules.SixNine && SchedulingRules.Week(w.Date) == week) <= 1);
                model.Add(LinearExpr.Sum(options.Where(x => p.Cells[x.Key.Cell].AnchorType == Domain.ShiftType.NIGHT && SchedulingRules.Week(p.Cells[x.Key.Cell].Date) == week).Select(x => x.Value))
                    + existing.Count(w => w.AnchorType == Domain.ShiftType.NIGHT && SchedulingRules.Week(w.Date) == week) <= 2);
            }
            foreach (var month in p.Cells.Where(c => SchedulingRules.Weekend(c.Date)).Select(c => SchedulingRules.Month(c.Semantic == SchedulingRules.OnCall ? SchedulingRules.Friday(c.Date) : c.Date)).Distinct())
            {
                var counted = options.Where(x => {
                    var c = p.Cells[x.Key.Cell];
                    if (!SchedulingRules.Weekend(c.Date)) return false;
                    var date = c.Semantic == SchedulingRules.OnCall ? SchedulingRules.Friday(c.Date) : c.Date;
                    if (SchedulingRules.Month(date) != month) return false;
                    // Existing half-pairs already reserve their allocation; equality supplies their mate.
                    if (c.Semantic == SchedulingRules.OnCall)
                        return !existing.Any(w => w.Semantic == SchedulingRules.OnCall && w.DefinitionId == c.DefinitionId && SchedulingRules.Friday(w.Date) == date)
                            && (c.Date.DayOfWeek == DayOfWeek.Friday || !p.Cells.Any(m => m.DefinitionId == c.DefinitionId && m.Date == date));
                    return true;
                });
                model.Add(LinearExpr.Sum(counted.Select(x => x.Value)) + SchedulingRules.Allocations(existing.Where(w => SchedulingRules.Weekend(w.Date) && SchedulingRules.AllocationMonth(w) == month)) <= 2);
            }
        }
        var fairness = new List<IntVar>();
        for (var ei = 0; ei < p.Employees.Length; ei++)
        {
            ct.ThrowIfCancellationRequested();
            var options = choices.Where(x => x.Key.Employee == ei).ToArray();
            var intervals = options.Select(x => {
                var c = p.Cells[x.Key.Cell];
                var start = c.Start.Ticks / TimeSpan.TicksPerSecond;
                var duration = (c.End.Ticks - c.Start.Ticks) / TimeSpan.TicksPerSecond + 1;
                return model.NewOptionalFixedSizeIntervalVar(start, duration, x.Value, $"i{x.Key.Cell}_{ei}");
            }).ToArray();
            model.AddNoOverlap(intervals);
            // Preserve the existing one-membership-per-canonical-shift invariant as well.
            foreach (var group in options.GroupBy(x => (p.Cells[x.Key.Cell].Date, p.Cells[x.Key.Cell].AnchorType)))
                model.Add(LinearExpr.Sum(group.Select(x => x.Value)) <= 1);
            if (options.Length == 0) continue;
            // Opportunity capacity is maximum non-touching interval count, not number of rows.
            var capacity = 0; var lastEnd = DateTime.MinValue;
            foreach (var c in options.Select(x => p.Cells[x.Key.Cell]).OrderBy(c => c.End).ThenBy(c => c.Start))
                if (c.Start > lastEnd) { capacity++; lastEnd = c.End; }
            var existing = fixedWork.Count(w => w.UserId == p.Employees[ei].Id);
            var load = model.NewIntVar(existing, existing + capacity, $"load{ei}");
            model.Add(load == LinearExpr.Sum(options.Select(x => x.Value)) + existing);
            var maxLoad = existing + capacity;
            var square = model.NewIntVar(0, (long)maxLoad * maxLoad, $"square{ei}");
            model.AddMultiplicationEquality(square, load, load);
            var cost = model.NewIntVar(0, (long)maxLoad * maxLoad * 1000, $"fairness{ei}");
            model.AddDivisionEquality(cost, square * 1000, Math.Max(1, maxLoad));
            fairness.Add(cost);
        }
        var expertFilled = LinearExpr.Sum(choices.Where(x => p.Cells[x.Key.Cell].RequiresShiftLeadQualification).Select(x => x.Value));
        var filled = LinearExpr.Sum(choices.Where(x => !p.Cells[x.Key.Cell].RequiresShiftLeadQualification).Select(x => x.Value));
        var preference = LinearExpr.Sum(choices.Where(x => p.Preferred(p.Employees[x.Key.Employee], p.Cells[x.Key.Cell])).Select(x => x.Value));
        var stability = LinearExpr.Sum(choices.Where(x => p.Work.Any(w => w.Replaceable && w.UserId == p.Employees[x.Key.Employee].Id
            && w.DefinitionId == p.Cells[x.Key.Cell].DefinitionId && w.Date == p.Cells[x.Key.Cell].Date)).Select(x => x.Value));
        var stages = new[] { expertFilled, filled, -LinearExpr.Sum(fairness), preference, stability };
        var proven = 0; var status = "TIMEOUT";
        Dictionary<(int Cell, int Employee), bool>? best = null;
        foreach (var objective in stages)
        {
            ct.ThrowIfCancellationRequested();
            var remaining = seconds - watch.Elapsed.TotalSeconds;
            if (remaining <= 0) break;
            model.Maximize(objective);
            var solver = new CpSolver { StringParameters = $"max_time_in_seconds:{remaining.ToString(CultureInfo.InvariantCulture)} num_search_workers:1 random_seed:42" };
            using var registration = ct.Register(solver.StopSearch);
            var result = solver.Solve(model);
            ct.ThrowIfCancellationRequested();
            if (result is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
            {
                best = choices.ToDictionary(x => x.Key, x => solver.BooleanValue(x.Value));
                status = "FEASIBLE";
                if (result != CpSolverStatus.Optimal) break;
                proven++;
                model.Add(objective == solver.Value(objective));
                model.ClearHints();
                foreach (var x in choices) model.AddHint(x.Value, best[x.Key] ? 1 : 0);
            }
            else
            {
                if (best is null && result == CpSolverStatus.Infeasible) throw new ApiException("EXISTING_CONSTRAINT_CONFLICT", 409);
                if (best is null) status = result == CpSolverStatus.Unknown ? "TIMEOUT" : "FAILED";
                break;
            }
        }
        if (best is null) return new(status, proven, []);
        if (proven == stages.Length) status = "OPTIMAL";
        var cells = p.Cells.Select((c, ci) => new GeneratedCell(c.DefinitionId, c.Date, c.Start, c.End, c.Required,
            fixedWork.Where(w => w.DefinitionId == c.DefinitionId && w.Date == c.Date).Select(w => new ScheduledEmployee(w.UserId, p.Employees.Single(e => e.Id == w.UserId).Name, true, w.Lead))
                .Concat(best.Where(x => x.Key.Cell == ci && x.Value).Select(x => { var e = p.Employees[x.Key.Employee]; return new ScheduledEmployee(e.Id, e.Name, false, false); }))
                .OrderBy(e => e.UserId).ToArray())).ToArray();
        var completed = fixedWork.Concat(cells.SelectMany(c => c.Employees.Where(e => !e.Protected).Select(e =>
            SchedulingRules.Proposed(e.UserId, p.Cells.Single(input => input.DefinitionId == c.DefinitionId && input.Date == c.Date)))));
        var finalViolations = SchedulingRules.Violations(completed, p.Year, p.Month);
        if (finalViolations.Length > 0) throw new ApiException(finalViolations[0].Code, 409);
        return new(status, proven, cells);
    }
}
