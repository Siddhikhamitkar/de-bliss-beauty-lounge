using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace DeBliss.Infrastructure.Staffing;

internal sealed class SalarySlipPdfFonts : IFontResolver
{
    private const string Family = "DeBliss Salary Slip Noto Sans";
    private const string RegularFace = "debliss-salary-noto-regular";
    private const string BoldFace = "debliss-salary-noto-bold";
    private const string ResourcePrefix = "DeBliss.Infrastructure.Staffing.Assets.";

    private static readonly Lazy<byte[]> Regular = new(() => ReadAsset("NotoSans-Regular.ttf"));
    private static readonly Lazy<byte[]> Bold = new(() => ReadAsset("NotoSans-Bold.ttf"));
    internal static readonly Lazy<byte[]> Branding = new(() => ReadAsset("DeBlissBranding.pdf"));
    private static readonly Lazy<bool> Registered = new(() =>
    {
        // PDFsharp requires process-wide registration before its first font is created.
        GlobalFontSettings.FontResolver = new SalarySlipPdfFonts();
        return true;
    });

    internal static XFont Create(double size, bool bold = false)
    {
        _ = Registered.Value;
        return new XFont(Family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        if (string.Equals(familyName, Family, StringComparison.Ordinal))
        {
            return new FontResolverInfo(isBold ? BoldFace : RegularFace);
        }

        return null;
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        RegularFace => Regular.Value,
        BoldFace => Bold.Value,
        _ => null
    };

    private static byte[] ReadAsset(string name)
    {
        using var stream = typeof(SalarySlipPdfFonts).Assembly.GetManifestResourceStream(ResourcePrefix + name)
            ?? throw new InvalidOperationException($"The embedded salary slip asset '{name}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
