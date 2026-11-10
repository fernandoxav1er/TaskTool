using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TaskTool.Services;

/// <summary>Desenha o ícone da bandeja: quadrado arredondado azul com a quantidade de tarefas ativas.</summary>
public static class TrayIconRenderer
{
    private static readonly Color Fundo = Color.FromArgb(0x4C, 0xC2, 0xFF);   // azul de destaque do Windows 11 (tema escuro)
    private static readonly Color Texto = Color.FromArgb(0x00, 0x1A, 0x2E);

    public static Icon Criar(int quantidade)
    {
        var tamanho = Math.Max(16, Native.GetSystemMetrics(Native.SM_CXSMICON));
        using var bmp = new Bitmap(tamanho, tamanho);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var r = new RectangleF(0.5f, 0.5f, tamanho - 1f, tamanho - 1f);
            using (var path = Arredondado(r, tamanho * 0.22f))
            using (var brush = new SolidBrush(Fundo))
                g.FillPath(brush, path);

            if (quantidade <= 0)
            {
                // check ✓
                using var pen = new Pen(Texto, tamanho * 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, [
                    new PointF(tamanho * 0.27f, tamanho * 0.52f),
                    new PointF(tamanho * 0.43f, tamanho * 0.68f),
                    new PointF(tamanho * 0.74f, tamanho * 0.34f),
                ]);
            }
            else
            {
                var txt = quantidade > 99 ? "99" : quantidade.ToString();
                var fator = txt.Length == 1 ? 0.62f : 0.50f;
                using var fonte = new Font("Segoe UI", tamanho * fator, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(Texto);
                using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(txt, fonte, brush, new RectangleF(0, tamanho * 0.03f, tamanho, tamanho), fmt);
            }
        }

        var hIcon = bmp.GetHicon();
        try
        {
            // Clona para um Icon que é dono do próprio handle e libera o original.
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath Arredondado(RectangleF r, float raio)
    {
        var d = raio * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
