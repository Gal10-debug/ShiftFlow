using Microsoft.AspNetCore.Identity;
namespace ShiftManager.Api;

public sealed class ApiException(string code, int status = 400, DateOnly? weekStartsOn = null, int? limit = null) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public DateOnly? WeekStartsOn { get; } = weekStartsOn;
    public int? Limit { get; } = limit;
}

public static class ApiErrors
{
    public static ApiError Error(string code) => new(code, code switch
    {
        "PERIOD_NOT_FOUND" => "לא נמצאה תקופת זמינות לחודש שנבחר.",
        "WINDOW_NOT_CONFIGURED" => "יש להגדיר חלון הגשה לפני הפתיחה.",
        "WINDOW_CLOSED" => "חלון ההגשה אינו פתוח. לא ניתן להגיש או לעדכן זמינות.",
        "INVALID_WINDOW_TIME" => "יש להזין מועד פתיחה ומועד סגירה מאוחר ממנו. לפתיחה נדרש מועד סגירה עתידי.",
        "WEEKLY_BLOCK_LIMIT" => "ניתן לחסום עד שלוש משמרות בשבוע, מיום ראשון עד שבת, גם בחציית חודשים.",
        "PERIOD_PUBLISHED" => "לא ניתן לשנות זמינות בתקופה שפורסמה.",
        "SHIFT_ALREADY_ASSIGNED" => "לא ניתן לחסום משמרת שכבר שובצת אליה. יש לפנות למנהל.",
        "NON_CANONICAL_SHIFT" => "נתוני המשמרות בחודש אינם תואמים לשעות העבודה. יש לפנות למנהל.",
        "HTTPS_REQUIRED" => "יש להתחבר למערכת באמצעות חיבור מאובטח.",
        "INVALID_CREDENTIALS" => "שם המשתמש או הסיסמה שגויים, או שלא ניתן להתחבר כעת.",
        "UNAUTHENTICATED" => "יש להתחבר למערכת כדי להמשיך.",
        "FORBIDDEN" => "אין לך הרשאה לביצוע הפעולה.",
        "PASSWORD_CHANGE_REQUIRED" => "יש להחליף את הסיסמה הראשונית כדי להמשיך.",
        "VALIDATION_ERROR" => "יש לבדוק את השדות שהוזנו ולמלא את כל שדות החובה.",
        "INVALID_PASSWORD" => "הסיסמה חייבת להכיל 12–128 תווים, אות גדולה וקטנה באנגלית, ספרה וסימן מיוחד.",
        "PASSWORD_CHANGE_FAILED" => "לא ניתן לשנות את הסיסמה. יש לבדוק את הסיסמה הנוכחית ואת דרישות הסיסמה החדשה.",
        "PASSWORD_REUSED" => "הסיסמה החדשה חייבת להיות שונה מהסיסמה הנוכחית.",
        "DUPLICATE_USERNAME" => "שם המשתמש כבר נמצא בשימוש.",
        "INVALID_ROLES" => "יש לבחור תפקיד אחד לפחות מתוך התפקידים המותרים.",
        "LAST_ADMIN" => "לא ניתן להסיר או להשבית את המנהל הפעיל האחרון.",
        "SELF_RESET" => "לשינוי הסיסמה שלך יש להשתמש במסך הפרופיל האישי.",
        "USER_NOT_FOUND" => "המשתמש המבוקש לא נמצא.",
        "CSRF_INVALID" => "תוקף הבקשה פג. יש לרענן את העמוד ולנסות שוב.",
        "RATE_LIMITED" => "בוצעו יותר מדי ניסיונות התחברות. יש להמתין ולנסות שוב.",
        "CONFLICT" => "המידע השתנה במקביל. יש לרענן ולנסות שוב.",
        "NOT_FOUND" => "הכתובת המבוקשת לא נמצאה.",
        "METHOD_NOT_ALLOWED" => "הפעולה אינה נתמכת בכתובת זו.",
        _ => "אירעה שגיאה. יש לנסות שוב מאוחר יותר."
    });

    public static ApiError Error(ApiException error)
    {
        var result = Error(error.Code);
        return result with { WeekStartsOn = error.WeekStartsOn, Limit = error.Limit };
    }

    public static void Ensure(IdentityResult result)
    {
        if (result.Succeeded) return;
        var codes = result.Errors.Select(x => x.Code).ToArray();
        if (codes.Contains("DuplicateUserName")) throw new ApiException("DUPLICATE_USERNAME", 409);
        if (codes.Contains("ConcurrencyFailure")) throw new ApiException("CONFLICT", 409);
        if (codes.Any(x => x.StartsWith("Password", StringComparison.Ordinal))) throw new ApiException("INVALID_PASSWORD");
        throw new ApiException("VALIDATION_ERROR");
    }
}
