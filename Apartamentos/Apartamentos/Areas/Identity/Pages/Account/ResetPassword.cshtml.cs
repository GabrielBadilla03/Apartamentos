using Apartamentos.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Apartamentos.Areas.Identity.Pages.Account;

public sealed class ResetPasswordModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Ingresá tu nueva contraseña.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "La contraseña debe tener al menos {2} caracteres.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Confirmá tu contraseña.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirmar contraseña")]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = "";

        [Required]
        public string Code { get; set; } = "";
    }

    public IActionResult OnGet(string? code = null)
    {
        if (string.IsNullOrEmpty(code)) return BadRequest("Se requiere un código de recuperación.");
        try { Input.Code = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return BadRequest("El código de recuperación es inválido."); }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var usuario = await userManager.FindByEmailAsync(Input.Email);
        if (usuario is null) return RedirectToPage("./ResetPasswordConfirmation");
        var result = await userManager.ResetPasswordAsync(usuario, Input.Code, Input.Password);
        if (result.Succeeded) return RedirectToPage("./ResetPasswordConfirmation");
        foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        return Page();
    }
}
