using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Valuator.Services;

namespace Valuator.Pages;

public class LoginModel : PageModel
{
    private readonly UserService _users;

    public LoginModel(UserService users)
    {
        _users = users;
    }

    [BindProperty]
    public string? Login { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToPage("/Index");
        }

        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(Password) || !_users.Verify(Login, Password))
        {
            // Одно и то же сообщение для неверного логина и неверного пароля
            ErrorMessage = "Неверный логин или пароль";
            ReturnUrl = returnUrl;
            return Page();
        }

        await UserService.SignInAsync(HttpContext, Login);

        // Перенаправляем только на локальные адреса (защита от open redirect)
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
