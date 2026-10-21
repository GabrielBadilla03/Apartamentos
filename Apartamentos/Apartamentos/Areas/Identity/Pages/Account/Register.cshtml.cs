using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Apartamentos.Areas.Identity.Pages.Account;

// El alta de usuarios se realiza EXCLUSIVAMENTE desde UsuariosController.
// Se bloquean GET y POST para que nadie pueda auto-registrarse.
public sealed class RegisterModel : PageModel
{
    public IActionResult OnGet() => NotFound();
    public IActionResult OnPost() => NotFound();
}
