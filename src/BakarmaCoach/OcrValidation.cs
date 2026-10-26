using System.IO;
using BakarmaCoach.Core;
using D = System.Drawing;

namespace BakarmaCoach;

public sealed record OcrValidationResult(bool Ok, IReadOnlyList<object> Cases, string? Error);
public static class OcrValidation
{
    public static OcrValidationResult Run(string directory)
    {
        var cases = new List<object>(); bool okay = true;
        try
        {
            using var ocr = new LocalOcr();
            foreach (string text in new[] { "Transcendent Bakarma", "Wave of Death", "Water Bomb", "Surging Wrath", "Fusillade", "Swipe", "Trample" })
            {
                using var image = new D.Bitmap(600, 64);
                using (var graphics = D.Graphics.FromImage(image))
                {
                    graphics.Clear(D.Color.FromArgb(16, 23, 36)); graphics.TextRenderingHint = D.Text.TextRenderingHint.AntiAliasGridFit;
                    using var font = new D.Font("Segoe UI", 22, D.FontStyle.Regular, D.GraphicsUnit.Pixel);
                    graphics.DrawString(text, font, D.Brushes.White, 12, 15);
                }
                var result = ocr.Read(image);
                bool recognized = text == "Transcendent Bakarma" ? ScreenText.IsBakarma(result.Text, result.Confidence) : ScreenText.Skill(result.Text, result.Confidence) is not null;
                okay &= recognized;
                cases.Add(new { expected = text, read = result.Text, confidence = result.Confidence, recognized });
            }
            return new(okay, cases, null);
        }
        catch (Exception ex) { return new(false, cases, ex.GetBaseException().ToString()); }
    }
}
