using DeBliss.Domain.Customers;

namespace DeBliss.UnitTests.Customers;

public class MobileNumberTests
{
    [Theory]
    [InlineData("+971 50 123 4567", "+971501234567")]
    [InlineData("+971501234567", "+971501234567")]
    [InlineData("971501234567", "+971501234567")]
    [InlineData("00971 50 123 4567", "+971501234567")]
    [InlineData("050 123 4567", "+971501234567")]
    [InlineData("050-123-4567", "+971501234567")]
    [InlineData("(050) 123 4567", "+971501234567")]
    [InlineData("501234567", "+971501234567")]
    [InlineData("+971 050 123 4567", "+971501234567")]
    [InlineData("0502801187", "+971502801187")]
    [InlineData("+971 4 123 4567", "+97141234567")]
    [InlineData("+91 98765 43210", "+919876543210")]
    [InlineData("+44 7700 900123", "+447700900123")]
    public void Every_way_a_number_is_written_becomes_one_canonical_form(string raw, string expected)
    {
        Assert.Equal(expected, MobileNumber.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12")]
    [InlineData("+971 50 123")]
    [InlineData("+971 50 123 45678")]
    [InlineData("1234567890123456")]
    [InlineData("---")]
    [InlineData("0123456789")]
    public void Unusable_input_has_no_canonical_form(string? raw)
    {
        Assert.Null(MobileNumber.Normalize(raw));
    }
}
