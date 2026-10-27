using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;
public sealed record TeamMemberResponse(Guid Id, string FirstName, string LastName, bool IsActive, bool IsEmployee);
public sealed record TeamResponse(Guid Id, string Name, Guid? LeaderUserId, Guid? DeputyUserId, long Version, TeamMemberResponse[] Members);
public sealed record UpdateTeamRequest([Required, StringLength(100), RegularExpression(@".*\S.*")] string Name,
    Guid? LeaderUserId, Guid? DeputyUserId, [Range(1, long.MaxValue)] long ExpectedVersion);
public sealed record MembershipRequest(Guid? TeamId);
