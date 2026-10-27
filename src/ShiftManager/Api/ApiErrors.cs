using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        "DEPUTY_REQUIRES_EMPLOYEE" => "למינוי סגן נדרש משתמש בעל תפקיד עובד.",
        "INVALID_TEAM_LEADERSHIP" => "ראש הצוות וסגנו חייבים להיות חברים פעילים בצוות ומשתמשים שונים.",
        "TEAM_LEADER_NOT_SCHEDULED" => "ראש צוות אינו משתתף בסידור ואינו נדרש להגיש זמינות.",
        "TEAM_LEADER_ASSIGNED" => "קיימים שיבוצים לראש צוות. יש להסדיר אותם לפני יצירת סידור חדש.",
        "ROSTER_CONFLICT" => "קיימות סתירות בסידור המחייבות טיפול מנהל.",
        "GENERATION_BUSY" => "קיימת הרצה פעילה או שהתור מלא. יש להמתין ולנסות שוב.",
        "PREVIEW_EXPIRED" => "תוקף התצוגה המקדימה פג. יש ליצור סידור מחדש.",
        "PREVIEW_NOT_READY" => "אין תצוגה מקדימה מוכנה לשמירה.",
        "STALE_PREVIEW" => "הנתונים השתנו מאז יצירת הסידור. יש ליצור סידור מחדש.",
        "PROTECTED_ASSIGNMENT_CONFLICT" => "שיבוץ מוגן אינו תואם לאילוצים או להגדרות. יש לבדוק את השיבוצים הקיימים.",
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
        "INVALID_SHIFT_TYPE_SETTINGS" => "יש להזין 51 הגדרות ייחודיות: שם באורך 1–100 תווים, סדר ייחודי 1–51, טווח שעות תקין ומספר עובדים בין 0 ל־1000.",
        "INVALID_USERNAME" => "שם המשתמש חייב להכיל 1–100 תווים: אותיות, ספרות או הסימנים . _ @ + - בלבד, ללא רווחים.",
        "INVALID_NAME" => "יש להזין שם באורך 1–100 תווים שאינו מכיל רק רווחים.",
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

    public static ApiError Validation(ActionContext context)
    {
        // Return only known field identifiers and stable codes, never attempted values,
        // JSON parser errors or exception messages (which may contain a password).
        var fields = new Dictionary<string, string>();
        foreach (var entry in context.ModelState.Where(x => x.Value?.Errors.Count > 0))
        {
            var name = entry.Key.Split('.').Last().ToLowerInvariant();
            var (field, code) = name switch
            {
                "firstname" => ("firstName", "INVALID_NAME"),
                "lastname" => ("lastName", "INVALID_NAME"),
                "username" => ("username", "INVALID_USERNAME"),
                "initialpassword" => ("initialPassword", "INVALID_PASSWORD"),
                "temporarypassword" => ("temporaryPassword", "INVALID_PASSWORD"),
                "roles" => ("roles", "INVALID_ROLES"),
                _ => ("", "")
            };
            if (field.Length > 0) fields[field] = code;
        }
        return Error("VALIDATION_ERROR") with { FieldErrors = fields.Count > 0 ? fields : null };
    }

    public static void Ensure(IdentityResult result)
    {
        if (result.Succeeded) return;
        var codes = result.Errors.Select(x => x.Code).ToArray();
        if (codes.Contains("InvalidUserName")) throw new ApiException("INVALID_USERNAME");
        if (codes.Contains("DuplicateUserName")) throw new ApiException("DUPLICATE_USERNAME", 409);
        if (codes.Contains("ConcurrencyFailure")) throw new ApiException("CONFLICT", 409);
        if (codes.Any(x => x.StartsWith("Password", StringComparison.Ordinal))) throw new ApiException("INVALID_PASSWORD");
        throw new ApiException("VALIDATION_ERROR");
    }
}
