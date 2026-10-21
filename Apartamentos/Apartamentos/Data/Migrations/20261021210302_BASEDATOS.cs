using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Apartamentos.Data.Migrations
{
    /// <inheritdoc />
    public partial class BASEDATOS : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Activo",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Apellidos",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "IdUsuarioAnterior",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nombre",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RutaImagen",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agua",
                columns: table => new
                {
                    id_agua = table.Column<long>(type: "bigint", nullable: false),
                    titular = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "date", nullable: true),
                    fecha_pagar = table.Column<DateTime>(type: "date", nullable: true),
                    fecha_inicio_pago = table.Column<DateTime>(type: "date", nullable: true),
                    pagar = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agua", x => x.id_agua);
                });

            migrationBuilder.CreateTable(
                name: "luz",
                columns: table => new
                {
                    id_luz = table.Column<long>(type: "bigint", nullable: false),
                    titular = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    fecha_vencimiento = table.Column<DateTime>(type: "date", nullable: true),
                    fecha_pagar = table.Column<DateTime>(type: "date", nullable: true),
                    fecha_inicio_pago = table.Column<DateTime>(type: "date", nullable: true),
                    pagar = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_luz", x => x.id_luz);
                });

            migrationBuilder.CreateTable(
                name: "montos_agua",
                columns: table => new
                {
                    id_monto = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    fecha_registro = table.Column<DateTime>(type: "date", nullable: false),
                    id_agua = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_montos_agua", x => x.id_monto);
                    table.ForeignKey(
                        name: "FK_montos_agua_agua_id_agua",
                        column: x => x.id_agua,
                        principalTable: "agua",
                        principalColumn: "id_agua",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "apartamento",
                columns: table => new
                {
                    id_apartamento = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ubicacion = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    descripcion_departamento = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    id_agua = table.Column<long>(type: "bigint", nullable: true),
                    id_luz = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_apartamento", x => x.id_apartamento);
                    table.ForeignKey(
                        name: "FK_apartamento_agua_id_agua",
                        column: x => x.id_agua,
                        principalTable: "agua",
                        principalColumn: "id_agua",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_apartamento_luz_id_luz",
                        column: x => x.id_luz,
                        principalTable: "luz",
                        principalColumn: "id_luz",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "montos_luz",
                columns: table => new
                {
                    id_monto = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    monto = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    fecha_registro = table.Column<DateTime>(type: "date", nullable: false),
                    id_luz = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_montos_luz", x => x.id_monto);
                    table.ForeignKey(
                        name: "FK_montos_luz_luz_id_luz",
                        column: x => x.id_luz,
                        principalTable: "luz",
                        principalColumn: "id_luz",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "encargado",
                columns: table => new
                {
                    id_encargado = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    apellidos = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    cedula = table.Column<int>(type: "int", nullable: false),
                    foto_cedula = table.Column<string>(type: "nvarchar(max)", maxLength: 4096, nullable: true),
                    id_apartamento = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_encargado", x => x.id_encargado);
                    table.ForeignKey(
                        name: "FK_encargado_apartamento_id_apartamento",
                        column: x => x.id_apartamento,
                        principalTable: "apartamento",
                        principalColumn: "id_apartamento",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_IdUsuarioAnterior",
                table: "AspNetUsers",
                column: "IdUsuarioAnterior",
                unique: true,
                filter: "[IdUsuarioAnterior] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_apartamento_id_agua",
                table: "apartamento",
                column: "id_agua");

            migrationBuilder.CreateIndex(
                name: "IX_apartamento_id_luz",
                table: "apartamento",
                column: "id_luz");

            migrationBuilder.CreateIndex(
                name: "IX_encargado_id_apartamento",
                table: "encargado",
                column: "id_apartamento");

            migrationBuilder.CreateIndex(
                name: "IX_montos_agua_id_agua_fecha_registro",
                table: "montos_agua",
                columns: new[] { "id_agua", "fecha_registro" });

            migrationBuilder.CreateIndex(
                name: "IX_montos_luz_id_luz_fecha_registro",
                table: "montos_luz",
                columns: new[] { "id_luz", "fecha_registro" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "encargado");

            migrationBuilder.DropTable(
                name: "montos_agua");

            migrationBuilder.DropTable(
                name: "montos_luz");

            migrationBuilder.DropTable(
                name: "apartamento");

            migrationBuilder.DropTable(
                name: "agua");

            migrationBuilder.DropTable(
                name: "luz");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_IdUsuarioAnterior",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Activo",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Apellidos",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "IdUsuarioAnterior",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Nombre",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "RutaImagen",
                table: "AspNetUsers");
        }
    }
}
