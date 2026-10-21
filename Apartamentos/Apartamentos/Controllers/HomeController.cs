using Apartamentos.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace Apartamentos.Controllers;
[Authorize]
public sealed class HomeController(ApplicationDbContext db):Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Apartamentos=await db.Apartamentos.CountAsync(ct);
        ViewBag.Encargados=await db.Encargados.CountAsync(ct);
        ViewBag.Aguas=await db.Aguas.CountAsync(ct);
        ViewBag.Luces=await db.Luces.CountAsync(ct);
        ViewBag.ApartamentosListado=await db.Apartamentos.Include(x=>x.Encargados).AsNoTracking().OrderBy(x=>x.Ubicacion).Take(20).ToListAsync(ct);
        ViewBag.EncargadosListado=await db.Encargados.Include(x=>x.Apartamento).AsNoTracking().OrderBy(x=>x.Nombre).Take(20).ToListAsync(ct);
        ViewBag.AguasListado=await db.Aguas.AsNoTracking().OrderBy(x=>x.IdAgua).Take(20).ToListAsync(ct);
        ViewBag.LucesListado=await db.Luces.AsNoTracking().OrderBy(x=>x.IdLuz).Take(20).ToListAsync(ct);
        return View();
    }
    [AllowAnonymous]
    public IActionResult Error()=>View("~/Views/Shared/Error.cshtml");
}
