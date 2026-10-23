using System.Globalization;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DeBliss.Infrastructure.Staffing;

public sealed class SalarySlipPdf : ISalarySlipPdf
{
    public byte[] Render(SalarySlip slip)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Salary slip";
        document.Info.Author = "De Bliss Beauty Lounge";
        document.Info.Creator = "DeBliss salary slip renderer";
        document.Info.Subject = $"Salary slip template {Text(slip.TemplateVersion)}";

        using var brandingStream = new MemoryStream(SalarySlipPdfFonts.Branding.Value, writable: false);
        using var branding = XPdfForm.FromStream(brandingStream);
        using (var layout = new Layout(document, branding, slip))
        {
            layout.Render();
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture) + " AED";

    private static string Days(decimal value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture);

    private sealed class Layout : IDisposable
    {
        private const double Margin = 40;
        private const double BodyTop = 130;
        private const double Gap = 22;
        private const double Padding = 8;
        private static readonly XBrush Ink = new XSolidBrush(XColor.FromArgb(61, 59, 58));
        private static readonly XBrush Beige = new XSolidBrush(XColor.FromArgb(227, 220, 212));
        private static readonly XPen Rule = new(XColor.FromArgb(118, 111, 104), 0.5);

        private readonly PdfDocument document;
        private readonly XPdfForm branding;
        private readonly SalarySlip slip;
        private readonly string period;
        private readonly string reference;
        private readonly XFont regular = SalarySlipPdfFonts.Create(9.5);
        private readonly XFont bold = SalarySlipPdfFonts.Create(9.5, bold: true);
        private readonly XFont small = SalarySlipPdfFonts.Create(8);
        private readonly XFont title = SalarySlipPdfFonts.Create(13, bold: true);
        private XGraphics graphics = null!;
        private double width;
        private double bottom;
        private double y;

        internal Layout(PdfDocument document, XPdfForm branding, SalarySlip slip)
        {
            this.document = document;
            this.branding = branding;
            this.slip = slip;
            period = new DateOnly(slip.SalaryYear, slip.SalaryMonth, 1)
                .ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            reference = FormattableString.Invariant($"SAL-{slip.SalaryYear:D4}{slip.SalaryMonth:D2}-{slip.Id:N}");
        }

        internal void Render()
        {
            NewPage();
            EmployeeAndAttendance();
            y += 20;
            EarningsAndDeductions();
            y += 18;
            NetSalary();
            y += 20;
            BankDetails();
            graphics.Dispose();
            graphics = null!;

            for (var i = 0; i < document.PageCount; i++)
            {
                var page = document.Pages[i];
                using var footer = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                var footerY = page.Height.Point - 35;
                footer.DrawLine(Rule, Margin, footerY - 10, page.Width.Point - Margin, footerY - 10);
                footer.DrawString("De Bliss Beauty Lounge • Salary slip", small, Ink,
                    new XRect(Margin, footerY, width - 100, 14), XStringFormats.TopLeft);
                footer.DrawString(FormattableString.Invariant($"Page {i + 1} of {document.PageCount}"), small, Ink,
                    new XRect(page.Width.Point - Margin - 100, footerY, 100, 14), XStringFormats.TopRight);
            }
        }

        private void NewPage()
        {
            graphics?.Dispose();
            var page = document.AddPage();
            page.Size = PageSize.A4;
            width = page.Width.Point - 2 * Margin;
            bottom = page.Height.Point - 58;
            graphics = XGraphics.FromPdfPage(page);

            // The branding-only form retains the original artwork and its aspect ratio.
            const double logoWidth = 204;
            graphics.DrawImage(branding, Margin - 4, 25,
                logoWidth, logoWidth * branding.PointHeight / branding.PointWidth);
            var headingX = Margin + width - 225;
            DrawLines(["PAYSLIP FOR THE MONTH"], regular, headingX, 39, 225);
            DrawLines([period.ToUpperInvariant()], title, headingX, 58, 225);
            DrawLines([$"Salary slip reference: {reference}"], small, Margin, 108, width);
            y = BodyTop;
        }

        private void EmployeeAndAttendance()
        {
            var left = new (string Label, string Value)[]
            {
                ("Employee name", Text(slip.EmployeeName)),
                ("Employee ID", Text(slip.EmployeeId)),
                ("Pay period", period),
                ("Pay date", slip.PayDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                ("Agreed total salary", slip.AgreedTotalSalary.HasValue ? Money(slip.AgreedTotalSalary.Value) : "—")
            };
            var right = new (string Label, string Value)[]
            {
                ("Working days", Days(slip.WorkingDays)),
                ("Total leave", Days(slip.TotalLeave)),
                ("Leave without pay", Days(slip.LeaveWithoutPay)),
                ("Late marks", slip.LateMarks.ToString(CultureInfo.InvariantCulture)),
                ("", "")
            };
            var column = (width - Gap) / 2;
            const double labelWidth = 101;
            var valueWidth = column - labelWidth;
            DetailHeadings(column);

            for (var i = 0; i < left.Length; i++)
            {
                var cells = new[]
                {
                    Wrap(left[i].Label, bold, labelWidth - 2 * Padding),
                    Wrap(left[i].Value, regular, valueWidth - 2 * Padding),
                    Wrap(right[i].Label, bold, labelWidth - 2 * Padding),
                    Wrap(right[i].Value, regular, valueWidth - 2 * Padding)
                };
                var height = Math.Max(32, cells.Max(lines => lines.Count) * LineHeight(bold) + 14);
                if (NeedsPage(height))
                {
                    NewPage();
                    DetailHeadings(column);
                }
                DrawLines(cells[0], bold, Margin + Padding, y + 7, labelWidth - 2 * Padding);
                DrawLines(cells[1], regular, Margin + labelWidth + Padding, y + 7, valueWidth - 2 * Padding);
                DrawLines(cells[2], bold, Margin + column + Gap + Padding, y + 7, labelWidth - 2 * Padding);
                DrawLines(cells[3], regular, Margin + column + Gap + labelWidth + Padding, y + 7, valueWidth - 2 * Padding);
                y += height;
                graphics.DrawLine(Rule, Margin, y, Margin + column, y);
                if (i < right.Length - 1)
                {
                    graphics.DrawLine(Rule, Margin + column + Gap, y, Margin + width, y);
                }
            }
        }

        private void DetailHeadings(double column)
        {
            Heading("EMPLOYEE DETAILS", Margin, column);
            Heading("ATTENDANCE", Margin + column + Gap, column);
            y += 27;
        }

        private void EarningsAndDeductions()
        {
            var rows = new[]
            {
                new[] { "Basic salary", Money(slip.BasicSalary), "Leave deduction", Money(slip.LeaveDeduction) },
                new[] { "House rent allowance", Money(slip.HouseRentAllowance), "Other deductions", Money(slip.OtherDeductions) },
                new[] { "Other allowance", Money(slip.OtherAllowance), "", "" },
                new[] { "Commission", Money(slip.Commission), "", "" }
            };
            double[] widths = [width * .27, width * .23, width * .27, width * .23];
            string[] headings = ["EARNINGS", "AMOUNT (AED)", "DEDUCTIONS", "AMOUNT (AED)"];
            var header = PrepareRow(headings, bold, widths);
            var first = PrepareRow(rows[0], regular, widths);
            if (NeedsPage(RowHeight(header, bold) + RowHeight(first, regular))) NewPage();
            TableRow(header, bold, widths, shaded: true);
            foreach (var row in rows)
            {
                var cells = PrepareRow(row, regular, widths);
                if (NeedsPage(RowHeight(cells, regular)))
                {
                    NewPage();
                    TableRow(header, bold, widths, shaded: true);
                }
                TableRow(cells, regular, widths, shaded: false);
            }
            var totals = PrepareRow(
                ["Total earnings", Money(slip.TotalEarnings), "Total deductions", Money(slip.TotalDeductions)], bold, widths);
            if (NeedsPage(RowHeight(totals, bold)))
            {
                NewPage();
                TableRow(header, bold, widths, shaded: true);
            }
            TableRow(totals, bold, widths, shaded: true);
        }

        private void NetSalary()
        {
            var amountWidth = width - 160;
            var lines = Wrap(Money(slip.NetSalary), title, amountWidth - 2 * Padding);
            var height = Math.Max(40, lines.Count * LineHeight(title) + 16);
            if (NeedsPage(height)) NewPage();
            graphics.DrawRectangle(Beige, Margin, y, width, height);
            DrawLines(["NET SALARY"], bold, Margin + 14, y + (height - LineHeight(bold)) / 2, 140);
            DrawLines(lines, title, Margin + 160, y + 8, amountWidth - 2 * Padding, right: true);
            y += height;
        }

        private void BankDetails()
        {
            var rows = new[]
            {
                ("Bank name", Text(slip.BankName)),
                ("Account name", Text(slip.AccountName)),
                ("Account number", Text(slip.BankAccountNumber)),
                ("IBAN", Text(slip.IbanNumber))
            };
            const double labelWidth = 112;
            var values = rows.Select(row => Wrap(row.Item2, regular, width - labelWidth - 2 * Padding)).ToArray();
            var heights = values.Select(lines => Math.Max(25, lines.Count * LineHeight(regular) + 10)).ToArray();
            // Keep bank details together when they fit on an otherwise empty page.
            var totalHeight = 29 + heights.Sum();
            if (totalHeight <= bottom - BodyTop && NeedsPage(totalHeight)) NewPage();
            else if (NeedsPage(29 + heights[0])) NewPage();
            BankHeading();
            for (var i = 0; i < rows.Length; i++)
            {
                if (NeedsPage(heights[i]))
                {
                    NewPage();
                    BankHeading();
                }
                DrawLines([rows[i].Item1], bold, Margin + Padding, y + 5, labelWidth - Padding);
                DrawLines(values[i], regular, Margin + labelWidth + Padding, y + 5, width - labelWidth - 2 * Padding);
                y += heights[i];
            }
        }

        private void BankHeading()
        {
            Heading("BANK DETAILS", Margin, width);
            y += 29;
        }

        private void Heading(string text, double x, double headingWidth)
        {
            graphics.DrawRectangle(Beige, x, y, headingWidth, 24);
            DrawLines([text], bold, x + Padding, y + 5, headingWidth - 2 * Padding);
        }

        private List<string>[] PrepareRow(string[] values, XFont font, double[] widths) =>
            values.Select((value, i) => Wrap(value, font, widths[i] - 2 * Padding)).ToArray();

        private static double RowHeight(List<string>[] cells, XFont font) =>
            Math.Max(36, cells.Max(lines => lines.Count) * LineHeight(font) + 16);

        private void TableRow(List<string>[] cells, XFont font, double[] widths, bool shaded)
        {
            var height = RowHeight(cells, font);
            var x = Margin;
            for (var i = 0; i < cells.Length; i++)
            {
                if (shaded) graphics.DrawRectangle(Beige, x, y, widths[i], height);
                graphics.DrawRectangle(Rule, x, y, widths[i], height);
                DrawLines(cells[i], font, x + Padding, y + 8, widths[i] - 2 * Padding, right: i % 2 == 1);
                x += widths[i];
            }
            y += height;
        }

        private bool NeedsPage(double height) => y + height > bottom;
        private static double LineHeight(XFont font) => font.GetHeight() + 1;

        private void DrawLines(IReadOnlyList<string> lines, XFont font, double x, double top, double lineWidth, bool right = false)
        {
            var height = LineHeight(font);
            foreach (var line in lines)
            {
                if (line.Length > 0)
                {
                    graphics.DrawString(line, font, Ink, new XRect(x, top, lineWidth, height),
                        right ? XStringFormats.TopRight : XStringFormats.TopLeft);
                }
                top += height;
            }
        }

        private List<string> Wrap(string value, XFont font, double lineWidth)
        {
            var lines = new List<string>();
            var line = "";
            foreach (var word in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (graphics.MeasureString(candidate, font).Width <= lineWidth)
                {
                    line = candidate;
                    continue;
                }
                if (line.Length > 0)
                {
                    lines.Add(line);
                    line = "";
                }
                // Break long identifiers without dropping characters or inserting hyphens.
                var elements = StringInfo.GetTextElementEnumerator(word);
                while (elements.MoveNext())
                {
                    var element = elements.GetTextElement();
                    if (line.Length > 0 && graphics.MeasureString(line + element, font).Width > lineWidth)
                    {
                        lines.Add(line);
                        line = "";
                    }
                    line += element;
                }
            }
            if (line.Length > 0 || lines.Count == 0) lines.Add(line);
            return lines;
        }

        public void Dispose() => graphics?.Dispose();
    }
}
