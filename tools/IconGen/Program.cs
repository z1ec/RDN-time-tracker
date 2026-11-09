using SkiaSharp;

// Генератор иконки WorkTime Tracker (F7.9): циферблат и галочка на индиго-фоне.
// Использование: dotnet run --project tools/IconGen [-- <папка вывода>]
// По умолчанию пишет в src/WorkTimeTracker.App/Assets: app.ico (слои 16…256) и icon-256.png.

var output = args.Length > 0 ? args[0] : Path.Combine(FindRepoRoot(), "src", "WorkTimeTracker.App", "Assets");
Directory.CreateDirectory(output);

int[] sizes = [16, 24, 32, 48, 64, 128, 256];
var layers = sizes.Select(s => (Size: s, Png: Render(s))).ToList();

await File.WriteAllBytesAsync(Path.Combine(output, "icon-256.png"), layers[^1].Png);
await File.WriteAllBytesAsync(Path.Combine(output, "app.ico"), BuildIco(layers));
Console.WriteLine($"Иконка записана в {output}: app.ico ({string.Join(", ", sizes)}), icon-256.png");
return 0;

static byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    var s = size / 256f; // все координаты заданы для 256×256

    // Фон: скруглённый квадрат с диагональным градиентом индиго.
    using (var bg = new SKPaint { IsAntialias = true })
    {
        bg.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(size, size),
            [new SKColor(0x63, 0x66, 0xF1), new SKColor(0x43, 0x38, 0xCA)],
            SKShaderTileMode.Clamp);
        var inset = size <= 32 ? 0 : 8 * s;
        c.DrawRoundRect(new SKRect(inset, inset, size - inset, size - inset), 56 * s, 56 * s, bg);
    }

    // Циферблат.
    var center = new SKPoint(118 * s, 122 * s);
    var radius = 78 * s;
    using (var face = new SKPaint { IsAntialias = true, Color = SKColors.White })
    {
        c.DrawCircle(center, radius, face);
    }

    // Риски на 12, 3, 6, 9 часов (на мелких размерах не видны — пропускаем).
    if (size >= 48)
    {
        using var tick = new SKPaint { IsAntialias = true, Color = new SKColor(0xC7, 0xD2, 0xFE), StrokeWidth = 8 * s, StrokeCap = SKStrokeCap.Round };
        for (var i = 0; i < 4; i++)
        {
            var a = i * MathF.PI / 2;
            var p1 = new SKPoint(center.X + (MathF.Sin(a) * radius * 0.78f), center.Y - (MathF.Cos(a) * radius * 0.78f));
            var p2 = new SKPoint(center.X + (MathF.Sin(a) * radius * 0.9f), center.Y - (MathF.Cos(a) * radius * 0.9f));
            c.DrawLine(p1, p2, tick);
        }
    }

    // Стрелки: 9 часов рабочего дня — часовая на 9, минутная на 12.
    using (var hand = new SKPaint { IsAntialias = true, Color = new SKColor(0x37, 0x30, 0xA3), StrokeWidth = Math.Max(1.5f, 14 * s), StrokeCap = SKStrokeCap.Round })
    {
        c.DrawLine(center, new SKPoint(center.X, center.Y - (radius * 0.62f)), hand);
        c.DrawLine(center, new SKPoint(center.X - (radius * 0.45f), center.Y), hand);
    }

    // Значок-галочка в правом нижнем углу.
    var badgeCenter = new SKPoint(186 * s, 186 * s);
    var badgeRadius = 50 * s;
    using (var ring = new SKPaint { IsAntialias = true, Color = new SKColor(0x43, 0x38, 0xCA) })
    {
        c.DrawCircle(badgeCenter, badgeRadius + (8 * s), ring);
    }

    using (var badge = new SKPaint { IsAntialias = true, Color = new SKColor(0x10, 0xB9, 0x81) })
    {
        c.DrawCircle(badgeCenter, badgeRadius, badge);
    }

    using (var check = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1.5f, 13 * s), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round })
    using (var path = new SKPath())
    {
        path.MoveTo(164 * s, 188 * s);
        path.LineTo(180 * s, 204 * s);
        path.LineTo(210 * s, 170 * s);
        c.DrawPath(path, check);
    }

    using var image = surface.Snapshot();
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// ICO с PNG-слоями (поддерживается Windows Vista и новее).
static byte[] BuildIco(IReadOnlyList<(int Size, byte[] Png)> layers)
{
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);
    w.Write((ushort)0);            // reserved
    w.Write((ushort)1);            // type: icon
    w.Write((ushort)layers.Count); // count
    var offset = 6 + (16 * layers.Count);
    foreach (var (size, png) in layers)
    {
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0);   // palette
        w.Write((byte)0);   // reserved
        w.Write((ushort)1); // planes
        w.Write((ushort)32); // bpp
        w.Write(png.Length);
        w.Write(offset);
        offset += png.Length;
    }

    foreach (var (_, png) in layers)
    {
        w.Write(png);
    }

    w.Flush();
    return ms.ToArray();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WorkTimeTracker.slnx")))
    {
        dir = dir.Parent;
    }

    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
