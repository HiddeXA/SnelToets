namespace SnelToets.Web.Infrastructure;

/// <summary>
/// The current (anonymous) learner. The id comes from the "st_learner" cookie set by
/// <see cref="LearnerCookieMiddleware"/> and is handed to the interactive circuit by App.razor → Routes.razor.
/// Inject this in pages: <c>@inject LearnerSession Learner</c> and use <c>Learner.Id</c>.
/// </summary>
public class LearnerSession
{
    public Guid Id { get; set; }
    public bool IsKnown => Id != Guid.Empty;
}

public class LearnerCookieMiddleware(RequestDelegate next)
{
    public const string CookieName = "st_learner";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!TryGet(context, out _))
        {
            var id = Guid.NewGuid();
            context.Response.Cookies.Append(CookieName, id.ToString(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddYears(2)
            });
            context.Items[CookieName] = id;
        }
        await next(context);
    }

    public static bool TryGet(HttpContext context, out Guid id)
    {
        if (context.Items.TryGetValue(CookieName, out var item) && item is Guid g) { id = g; return true; }
        return Guid.TryParse(context.Request.Cookies[CookieName], out id);
    }
}

/// <summary>
/// Whether the current circuit has unlocked the "Beheer" (management) pages.
/// When configuration "Beheer:Pincode" is empty, management is open to everyone.
/// </summary>
public class AdminSession(IConfiguration config)
{
    private readonly string? _pin = config["Beheer:Pincode"];
    public bool PinRequired => !string.IsNullOrWhiteSpace(_pin);
    public bool IsUnlocked { get; private set; }
    public bool HasAccess => !PinRequired || IsUnlocked;

    public bool TryUnlock(string? pin)
    {
        if (!PinRequired || string.Equals(pin?.Trim(), _pin!.Trim(), StringComparison.Ordinal)) IsUnlocked = true;
        return HasAccess;
    }
}
