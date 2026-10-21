using System.Globalization;
using System.Text;

namespace Apartamentos.Services;

/// <summary>
/// Creador de PDFs sencillos (texto + tabla) sin paquetes ni DLL graficas nativas.
/// PDF 1.4 / WinAnsiEncoding / Helvetica. No admite imagenes ni caracteres fuera
/// del repertorio Windows-1252, que se sustituyen por '?'.
/// </summary>
internal static class GeneradorPdfSeguro
{
    private static readonly Encoding Codigo = Encoding.Latin1;
    private const int Alto = 842;
    private const int Ancho = 595;
    private static readonly int[] Posiciones = { 38, 105, 339, 427, 478 };
    private static readonly int[] Limites = { 12, 44, 15, 10, 18 };

    public static byte[] Crear(string titulo, IReadOnlyList<string[]> filas)
    {
        var paginas = new List<string>();
        var dibujo = new StringBuilder();
        int pagina = 1;
        int y = ComenzarPagina(dibujo, titulo, pagina);

        foreach (var fila in filas)
        {
            var lineas = new List<string>[5];
            for (int c = 0; c < 5; c++) lineas[c] = Partir(fila[c], Limites[c], c == 1 ? 4 : 2);
            int max = lineas.Max(l => l.Count);
            int altura = Math.Max(21, max * 11 + 10);
            if (y - altura < 55)
            {
                paginas.Add(dibujo.ToString());
                dibujo.Clear();
                y = ComenzarPagina(dibujo, titulo, ++pagina);
            }
            for (int c = 0; c < 5; c++)
            {
                for (int l = 0; l < lineas[c].Count; l++)
                    Texto(dibujo, Posiciones[c], y - 11 * l, 8, lineas[c][l]);
            }
            dibujo.AppendLine($"0.85 G 0.45 w 38 {y - altura + 4} m 558 {y - altura + 4} l S");
            y -= altura;
        }
        paginas.Add(dibujo.ToString());
        return EmpaquetarPdf(paginas);
    }

    private static int ComenzarPagina(StringBuilder contenido, string titulo, int pagina)
    {
        Texto(contenido, 38, 803, 15, titulo);
        Texto(contenido, 38, 784, 8, $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}   |   Pagina {pagina}");
        contenido.AppendLine("0 G 0.75 w 38 771 m 558 771 l S");
        var titulos = new[] { "NIS", "Apartamentos", "Vencimiento", "Pagar", "Monto" };
        for (int i = 0; i < 5; i++) Texto(contenido, Posiciones[i], 752, 8, titulos[i]);
        contenido.AppendLine("0 G 0.65 w 38 743 m 558 743 l S");
        return 726;
    }

    private static void Texto(StringBuilder b, int x, int y, int tamano, string texto)
    {
        var salida = PrepararTexto(texto);
        b.Append("BT /F1 ").Append(tamano.ToString(CultureInfo.InvariantCulture))
            .Append(" Tf 1 0 0 1 ").Append(x.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(y.ToString(CultureInfo.InvariantCulture))
            .Append(" Tm (").Append(salida).AppendLine(") Tj ET");
    }

    private static string PrepararTexto(string? texto)
    {
        var sb = new StringBuilder();
        foreach (var c in texto ?? "")
        {
            if (c == '\r' || c == '\n' || c == '\t') { sb.Append(' '); continue; }
            if (c == '\\' || c == '(' || c == ')') sb.Append('\\');
            if (c >= 32 && c <= 255) sb.Append(c);
            else sb.Append('?');
        }
        return sb.ToString();
    }

    private static List<string> Partir(string? texto, int max, int maxLineas)
    {
        var sinSaltos = (texto ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        var result = new List<string>();
        while (sinSaltos.Length > 0 && result.Count < maxLineas)
        {
            if (sinSaltos.Length <= max) { result.Add(sinSaltos); break; }
            int corte = sinSaltos.LastIndexOf(' ', max, max);
            if (corte < max / 2) corte = max;
            var trozo = sinSaltos[..corte].TrimEnd();
            result.Add(trozo);
            sinSaltos = sinSaltos[corte..].TrimStart();
            if (result.Count == maxLineas && sinSaltos.Length > 0)
                result[^1] = result[^1].Length >= max - 3 ? result[^1][..(max-3)] + "..." : result[^1] + "...";
        }
        if (result.Count == 0) result.Add("");
        return result;
    }

    private static byte[] EmpaquetarPdf(IReadOnlyList<string> paginas)
    {
        // Objetos: catalogo=1, lista paginas=2, fuente=3, pares pagina/contenido a partir del 4.
        int total = 3 + paginas.Count * 2;
        byte[][] objetos = new byte[total + 1][];
        objetos[1] = Bytes("<< /Type /Catalog /Pages 2 0 R >>");
        string kids = string.Join(" ", Enumerable.Range(0, paginas.Count).Select(i => $"{4 + 2 * i} 0 R"));
        objetos[2] = Bytes($"<< /Type /Pages /Kids [{kids}] /Count {paginas.Count} >>");
        objetos[3] = Bytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        for (int i = 0; i < paginas.Count; i++)
        {
            int num = 4 + 2 * i;
            objetos[num] = Bytes($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Ancho} {Alto}] " +
                $"/Resources << /Font << /F1 3 0 R >> >> /Contents {num + 1} 0 R >>");
            byte[] streams = Bytes(paginas[i]);
            using var content = new MemoryStream();
            Escribir(content, $"<< /Length {streams.Length} >>\nstream\n");
            content.Write(streams, 0, streams.Length);
            Escribir(content, "\nendstream");
            objetos[num + 1] = content.ToArray();
        }

        using var pdf = new MemoryStream();
        Escribir(pdf, "%PDF-1.4\n");
        long[] offsets = new long[total + 1];
        for (int id = 1; id <= total; id++)
        {
            offsets[id] = pdf.Position;
            Escribir(pdf, $"{id} 0 obj\n");
            pdf.Write(objetos[id], 0, objetos[id].Length);
            Escribir(pdf, "\nendobj\n");
        }
        long inicioXref = pdf.Position;
        Escribir(pdf, $"xref\n0 {total + 1}\n0000000000 65535 f \n");
        for (int id = 1; id <= total; id++) Escribir(pdf, $"{offsets[id]:D10} 00000 n \n");
        Escribir(pdf, $"trailer\n<< /Size {total + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n");
        return pdf.ToArray();
    }

    private static byte[] Bytes(string s) => Codigo.GetBytes(s);
    private static void Escribir(Stream destino, string s)
    {
        var bytes = Bytes(s);
        destino.Write(bytes, 0, bytes.Length);
    }
}
