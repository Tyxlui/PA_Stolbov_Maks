using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valuator.Services;

namespace Valuator.Pages;

public class RegisterModel : PageModel
{
    private readonly UserService _users;

    public RegisterModel(UserService users)
    {
        _users = users;
    }

    [BindProperty]
    public string? Login { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    [BindProperty]
    public string? ConfirmPassword { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!UserService.IsValidLogin(Login))
        {
            ErrorMessage = "Логин: от 3 до 32 символов, латинские буквы, цифры и знаки . _ -";
            return Page();
        }

        if (Password is null || Password.Length < 6 || Password.Length > 128)
        {
            ErrorMessage = "Пароль: от 6 до 128 символов";
            return Page();
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Пароли не совпадают";
            return Page();
        }

        string login = UserService.Normalize(Login!);

        if (!_users.Register(login, Password))
        {
            ErrorMessage = "Этот логин уже занят";
            return Page();
        }

        // После регистрации сразу входим в систему
        await UserService.SignInAsync(HttpContext, login);
        return RedirectToPage("/Index");
    }
}
