using System.Globalization;
using System.Text;

namespace Apartamentos.Services;

/// <summary>
/// Tabla PDF equivalente a los reportes iText de Proyecto Temporal:
/// A4, titulo de 18 puntos, columnas ID/Vencimiento/Apartamentos/Pagar/Monto
/// con anchos 80/140/100/50/130, celdas con borde y encabezado por pagina.
/// No depende de librerias graficas nativas (apto para IIS).
/// </summary>
internal static class GeneradorPdfSeguro
{
    private static readonly Encoding Codigo = Encoding.Latin1;
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;
    private const int Alto = 842;
    private const int Ancho = 595;
    private const float Margen = 36f;
    private const float AltoLinea = 13f;
    private static readonly float[] Anchos = { 80f, 140f, 100f, 50f, 130f };
    private static readonly string[] Encabezados =
        { "ID", "Vencimiento(Año/Mes/dia)", "Apartamentos", "Pagar", "Monto" };

    public static byte[] Crear(string titulo, IReadOnlyList<string[]> filas)
    {
        var paginas = new List<string>();
        var contenido = new StringBuilder();
        float y = ComenzarPagina(contenido, titulo);

        foreach (var fila in filas)
        {
            var lineas = new List<string>[Anchos.Length];
            for (int i = 0; i < Anchos.Length; i++)
                lineas[i] = PartirTexto(i < fila.Length ? fila[i] : "", Anchos[i] - 9f, 11f);

            float alto = Math.Max(28f, lineas.Max(l => l.Count) * AltoLinea + 11f);
            if (y - alto < Margen && contenido.Length > 0)
            {
                paginas.Add(contenido.ToString());
                contenido.Clear();
                y = ComenzarPagina(contenido, null);
            }

            DibujarFila(contenido, y, alto, lineas, false);
            y -= alto;
        }

        paginas.Add(contenido.ToString());
        return EmpaquetarPdf(paginas);
    }

    private static float ComenzarPagina(StringBuilder contenido, string? titulo)
    {
        float y = Alto - Margen;
        if (!string.IsNullOrEmpty(titulo))
        {
            Texto(contenido, Margen, y - 20f, 18f, titulo, true);
            y -= 43f;
        }

        var cabeceras = Encabezados.Select((s, i) =>
            PartirTexto(s, Anchos[i] - 9f, 11f)).ToArray();
        float altoCabecera = Math.Max(29f, cabeceras.Max(l => l.Count) * AltoLinea + 11f);
        DibujarFila(contenido, y, altoCabecera, cabeceras, true);
        return y - altoCabecera;
    }

    private static void DibujarFila(
        StringBuilder contenido, float arriba, float altura,
        IReadOnlyList<List<string>> celdas, bool encabezado)
    {
        float x = Margen;
        for (int col = 0; col < Anchos.Length; col++)
        {
            contenido.Append("0 G 0.5 w ")
                .Append(N(x)).Append(' ')
                .Append(N(arriba - altura)).Append(' ')
                .Append(N(Anchos[col])).Append(' ')
                .Append(N(altura)).AppendLine(" re S");

            float y = arriba - 14f;
            foreach (var linea in celdas[col])
            {
                Texto(contenido, x + 4f, y, 11f, linea, encabezado);
                y -= AltoLinea;
            }
            x += Anchos[col];
        }
    }

    private static List<string> PartirTexto(string? texto, float ancho, float fuente)
    {
        var resultado = new List<string>();
        foreach (string original in (texto ?? "").Replace("\r", "").Split('\n'))
        {
            var restante = original.Trim();
            if (restante.Length == 0)
            {
                resultado.Add("");
                continue;
            }

            while (restante.Length > 0)
            {
                int caracteres = 0;
                float usado = 0;
                while (caracteres < restante.Length)
                {
                    float siguiente = AnchoAproximado(restante[caracteres], fuente);
                    if (usado + siguiente > ancho && caracteres > 0) break;
                    usado += siguiente;
                    caracteres++;
                }

                if (caracteres >= restante.Length)
                {
                    resultado.Add(restante);
                    break;
                }

                int espacio = restante.LastIndexOf(' ', Math.Max(0, caracteres - 1), caracteres);
                int corte = espacio > 0 ? espacio : Math.Max(1, caracteres);
                resultado.Add(restante[..corte].TrimEnd());
                restante = restante[corte..].TrimStart();
            }
        }
        return resultado.Count == 0 ? new List<string> { "" } : resultado;
    }

    private static float AnchoAproximado(char c, float fuente)
    {
        // Medidas aproximadas de Helvetica, solo para el salto de linea.
        float fraccion = c switch
        {
            ' ' => .28f,
            'i' or 'l' or 'I' or '.' or ',' or ':' or ';' or '!' or '|' => .26f,
            'm' or 'w' or 'M' or 'W' => .82f,
            'f' or 'r' or 't' or 'j' => .33f,
            '(' or ')' or '/' or '-' => .36f,
            _ => .55f
        };
        return fuente * fraccion;
    }

    private static void Texto(
        StringBuilder contenido, float x, float y, float tamano, string? texto, bool negrita)
    {
        contenido.Append("BT /").Append(negrita ? "F2" : "F1").Append(' ')
            .Append(N(tamano)).Append(" Tf 1 0 0 1 ")
            .Append(N(x)).Append(' ').Append(N(y))
            .Append(" Tm (").Append(PrepararTexto(texto)).AppendLine(") Tj ET");
    }

    private static string PrepararTexto(string? texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto ?? "")
        {
            if (c == '\\' || c == '(' || c == ')') sb.Append('\\');
            if (c >= 32 && c <= 255) sb.Append(c);
            else if (c == '\t') sb.Append(' ');
            else sb.Append('?');
        }
        return sb.ToString();
    }

    private static string N(float numero) => numero.ToString("0.##", Invariante);

    private static byte[] EmpaquetarPdf(IReadOnlyList<string> paginas)
    {
        // Catalogo=1, Pages=2, Helvetica=3, Helvetica-Bold=4; paginas desde 5.
        int total = 4 + paginas.Count * 2;
        var objetos = new byte[total + 1][];
        objetos[1] = Bytes("<< /Type /Catalog /Pages 2 0 R >>");
        string hijos = string.Join(" ", Enumerable.Range(0, paginas.Count)
            .Select(i => $"{5 + i * 2} 0 R"));
        objetos[2] = Bytes($"<< /Type /Pages /Kids [{hijos}] /Count {paginas.Count} >>");
        objetos[3] = Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        objetos[4] = Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

        for (int i = 0; i < paginas.Count; i++)
        {
            int numero = 5 + i * 2;
            objetos[numero] = Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Ancho} {Alto}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {numero + 1} 0 R >>");

            byte[] stream = Bytes(paginas[i]);
            using var contenido = new MemoryStream();
            Escribir(contenido, $"<< /Length {stream.Length} >>\nstream\n");
            contenido.Write(stream, 0, stream.Length);
            Escribir(contenido, "\nendstream");
            objetos[numero + 1] = contenido.ToArray();
        }

        using var pdf = new MemoryStream();
        Escribir(pdf, "%PDF-1.4\n");
        var posiciones = new long[total + 1];
        for (int id = 1; id <= total; id++)
        {
            posiciones[id] = pdf.Position;
            Escribir(pdf, $"{id} 0 obj\n");
            pdf.Write(objetos[id], 0, objetos[id].Length);
            Escribir(pdf, "\nendobj\n");
        }
        long inicioXref = pdf.Position;
        Escribir(pdf, $"xref\n0 {total + 1}\n0000000000 65535 f \n");
        for (int id = 1; id <= total; id++)
            Escribir(pdf, $"{posiciones[id]:D10} 00000 n \n");
        Escribir(pdf, $"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] Bytes(string texto) => Codigo.GetBytes(texto);

    private static void Escribir(Stream destino, string texto)
    {
        byte[] bytes = Bytes(texto);
        destino.Write(bytes, 0, bytes.Length);
    }
}
