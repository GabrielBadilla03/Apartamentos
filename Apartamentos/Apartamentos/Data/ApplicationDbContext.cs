using Apartamentos.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Apartamentos.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Agua> Aguas => Set<Agua>();
    public DbSet<Luz> Luces => Set<Luz>();
    public DbSet<Apartamento> Apartamentos => Set<Apartamento>();
    public DbSet<Encargado> Encargados => Set<Encargado>();
    public DbSet<MontoAgua> MontosAgua => Set<MontoAgua>();
    public DbSet<MontoLuz> MontosLuz => Set<MontoLuz>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ApplicationUser>().HasIndex(x => x.IdUsuarioAnterior).IsUnique().HasFilter("[IdUsuarioAnterior] IS NOT NULL");
        b.Entity<Agua>(e => {
            e.ToTable("agua"); e.HasKey(x => x.IdAgua); e.Property(x => x.IdAgua).HasColumnName("id_agua").ValueGeneratedNever();
            e.Property(x => x.Titular).HasColumnName("titular"); e.Property(x => x.Pagar).HasColumnName("pagar");
            e.Property(x => x.FechaVencimiento).HasColumnName("fecha_vencimiento").HasColumnType("date");
            e.Property(x => x.FechaPagar).HasColumnName("fecha_pagar").HasColumnType("date");
            e.Property(x => x.FechaInicioPago).HasColumnName("fecha_inicio_pago").HasColumnType("date");
        });
        b.Entity<Luz>(e => {
            e.ToTable("luz"); e.HasKey(x => x.IdLuz); e.Property(x => x.IdLuz).HasColumnName("id_luz").ValueGeneratedNever();
            e.Property(x => x.Titular).HasColumnName("titular"); e.Property(x => x.Pagar).HasColumnName("pagar");
            e.Property(x => x.FechaVencimiento).HasColumnName("fecha_vencimiento").HasColumnType("date");
            e.Property(x => x.FechaPagar).HasColumnName("fecha_pagar").HasColumnType("date");
            e.Property(x => x.FechaInicioPago).HasColumnName("fecha_inicio_pago").HasColumnType("date");
        });
        b.Entity<Apartamento>(e => {
            e.ToTable("apartamento"); e.Property(x => x.IdApartamento).HasColumnName("id_apartamento");
            e.Property(x => x.Ubicacion).HasColumnName("ubicacion");
            e.Property(x => x.DescripcionDepartamento).HasColumnName("descripcion_departamento");
            e.Property(x => x.IdAgua).HasColumnName("id_agua");
            e.Property(x => x.IdLuz).HasColumnName("id_luz");
            e.HasOne(x => x.Agua).WithMany(x => x.Apartamentos).HasForeignKey(x => x.IdAgua).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Luz).WithMany(x => x.Apartamentos).HasForeignKey(x => x.IdLuz).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Encargado>(e => {
            e.ToTable("encargado"); e.Property(x => x.IdEncargado).HasColumnName("id_encargado");
            e.Property(x => x.Nombre).HasColumnName("nombre"); e.Property(x => x.Apellidos).HasColumnName("apellidos");
            e.Property(x => x.Cedula).HasColumnName("cedula"); e.Property(x => x.FotoCedula).HasColumnName("foto_cedula");
            e.Property(x => x.IdApartamento).HasColumnName("id_apartamento");
            e.HasOne(x => x.Apartamento).WithMany(x => x.Encargados).HasForeignKey(x => x.IdApartamento).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MontoAgua>(e => {
            e.ToTable("montos_agua"); e.Property(x => x.IdMonto).HasColumnName("id_monto");
            e.Property(x => x.Monto).HasColumnName("monto").HasPrecision(18,2);
            e.Property(x => x.FechaRegistro).HasColumnName("fecha_registro").HasColumnType("date");
            e.Property(x => x.IdAgua).HasColumnName("id_agua");
            e.HasOne(x => x.Agua).WithMany(x => x.MontosAgua).HasForeignKey(x => x.IdAgua).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IdAgua, x.FechaRegistro });
        });
        b.Entity<MontoLuz>(e => {
            e.ToTable("montos_luz"); e.Property(x => x.IdMonto).HasColumnName("id_monto");
            e.Property(x => x.Monto).HasColumnName("monto").HasPrecision(18,2);
            e.Property(x => x.FechaRegistro).HasColumnName("fecha_registro").HasColumnType("date");
            e.Property(x => x.IdLuz).HasColumnName("id_luz");
            e.HasOne(x => x.Luz).WithMany(x => x.MontosLuz).HasForeignKey(x => x.IdLuz).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.IdLuz, x.FechaRegistro });
        });
    }
}
