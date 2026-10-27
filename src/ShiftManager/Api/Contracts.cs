using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;

public sealed record LoginRequest([Required, StringLength(100)] string Username, [Required, StringLength(128)] string Password);
public sealed record ChangePasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record ProfileRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string FirstName,
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string LastName,
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{N}._@+\-]+$")] string Username, bool? IsShiftLeadQualified = null);
public sealed record CreateUserRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string FirstName,
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string LastName,
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{N}._@+\-]+$")] string Username,
    [Required, StringLength(128, MinimumLength = 12)] string InitialPassword,
    [Required, MinLength(1), MaxLength(3)] string[] Roles, bool IsActive = true, bool IsShiftLeadQualified = false);
public sealed record RolesRequest([Required, MinLength(1), MaxLength(3)] string[] Roles);
public sealed record StatusRequest([Required] bool? IsActive);
public sealed record ResetPasswordRequest([Required, StringLength(128, MinimumLength = 12)] string TemporaryPassword);
public sealed record UserResponse(Guid Id, string FirstName, string LastName, string Username, bool IsActive,
    bool MustChangePassword, DateTime CreatedAt, string[] Roles, bool IsShiftLeadQualified = false, bool CanManageTeamAvailability = false, bool IsTeamLeader = false);
public sealed record UserPage(UserResponse[] Items, int TotalCount, int Page, int PageSize);
public sealed record ApiError(string Code, string Message)
{
    public DateOnly? WeekStartsOn { get; init; }
    public int? Limit { get; init; }
    public Dictionary<string, string>? FieldErrors { get; init; }
}
