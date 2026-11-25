using System.Globalization;
using Microsoft.AspNetCore.Identity;

namespace CarTracker.Api.Services;

/// <summary>
/// Two UI languages, English (default) and German. The SPA sends the user's language as Accept-Language;
/// request localization puts it into CurrentUICulture, and every user-facing message picks its text with <see cref="T"/>.
/// </summary>
public static class Loc
{
    public static readonly string[] Languages = ["en", "de"];
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static bool IsGerman => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";
    public static string T(string en, string de) => IsGerman ? de : en;
    public static IResult Problem(string en, string de, int status) => Results.Problem(T(en, de), statusCode: status);

    /// <summary>Money for messages, formatted for the current language.</summary>
    public static string Money(decimal v) => IsGerman ? v.ToString("N2", German) + " €" : v.ToString("N2", CultureInfo.InvariantCulture) + " €";

    public static string? Normalize(string? lang) => lang?.Trim().ToLowerInvariant() switch
    {
        ['d', 'e', ..] => "de",
        ['e', 'n', ..] => "en",
        _ => null,
    };

    /// <summary>Runs code (e.g. in a background job) with a user's language as UI culture.</summary>
    public static IDisposable Use(string? lang)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = Normalize(lang) == "de" ? German : CultureInfo.GetCultureInfo("en");
        return new Restore(previous);
    }

    private sealed class Restore(CultureInfo previous) : IDisposable
    {
        public void Dispose() => CultureInfo.CurrentUICulture = previous;
    }
}

/// <summary>Identity's registration errors (password too short, email taken, …) in the user's language.</summary>
public class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = Loc.T($"The password needs at least {length} characters.", $"Das Passwort braucht mindestens {length} Zeichen."),
    };
    public override IdentityError DuplicateEmail(string email) => new()
    {
        Code = nameof(DuplicateEmail),
        Description = Loc.T($"There is already an account with {email}.", $"Es gibt schon ein Konto mit {email}."),
    };
    public override IdentityError DuplicateUserName(string userName) => new()
    {
        Code = nameof(DuplicateUserName),
        Description = Loc.T($"There is already an account with {userName}.", $"Es gibt schon ein Konto mit {userName}."),
    };
    public override IdentityError InvalidEmail(string? email) => new()
    {
        Code = nameof(InvalidEmail),
        Description = Loc.T($"“{email}” is not a valid email address.", $"„{email}“ ist keine gültige E-Mail-Adresse."),
    };
    public override IdentityError InvalidUserName(string? userName) => new()
    {
        Code = nameof(InvalidUserName),
        Description = Loc.T($"“{userName}” is not a valid email address.", $"„{userName}“ ist keine gültige E-Mail-Adresse."),
    };
}
