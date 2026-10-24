using System.ComponentModel.DataAnnotations;
namespace ShiftManager.Api;

public sealed record LoginRequest([Required, StringLength(100)] string Username, [Required, StringLength(128)] string Password);
public sealed record ChangePasswordRequest([Required, StringLength(128)] string CurrentPassword, [Required, StringLength(128, MinimumLength = 12)] string NewPassword);
public sealed record ProfileRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string FirstName,
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string LastName,
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{N}._@+\-]+$")] string Username);
public sealed record CreateUserRequest(
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string FirstName,
    [Required, StringLength(100), RegularExpression(@".*\S.*")] string LastName,
    [Required, StringLength(100), RegularExpression(@"^[\p{L}\p{N}._@+\-]+$")] string Username,
    [Required, StringLength(128, MinimumLength = 12)] string InitialPassword,
    [Required, MinLength(1), MaxLength(3)] string[] Roles, bool IsActive = true);
public sealed record RolesRequest([Required, MinLength(1), MaxLength(3)] string[] Roles);
public sealed record StatusRequest([Required] bool? IsActive);
public sealed record ResetPasswordRequest([Required, StringLength(128, MinimumLength = 12)] string TemporaryPassword);
public sealed record UserResponse(Guid Id, string FirstName, string LastName, string Username, bool IsActive,
    bool MustChangePassword, DateTime CreatedAt, string[] Roles);
public sealed record UserPage(UserResponse[] Items, int TotalCount, int Page, int PageSize);
public sealed record ApiError(string Code, string Message);
