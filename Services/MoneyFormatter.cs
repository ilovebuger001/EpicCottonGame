using System.Numerics;

namespace EpicCottonGame.Services;

/// <summary>Formats arbitrarily large BigInteger values without converting them to floating point.</summary>
public static class MoneyFormatter
{
    static readonly string[] Suffixes =
    {
        "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No",
        "Dc", "UDc", "DDc", "TDc", "QaDc", "QiDc", "SxDc", "SpDc", "OcDc", "NoDc", "Vg"
    };

    public static string Format(BigInteger value)
    {
        var negative = value < 0;
        if (negative) value = BigInteger.Abs(value);
        if (value < 1000) return (negative ? "-" : "") + value.ToString();

        var digits = value.ToString();
        var group = (digits.Length - 1) / 3;
        if (group >= Suffixes.Length)
        {
            var first = digits[0];
            var next = digits.Length > 1 ? digits[1].ToString() : "0";
            return $"{(negative ? "-" : "")}{first}.{next}e{digits.Length - 1}";
        }

        var divisor = BigInteger.Pow(1000, group);
        var whole = value / divisor;
        var remainder = value % divisor;
        var tenths = (remainder * 10) / divisor;
        var display = whole.ToString();
        if (tenths > 0) display += $".{tenths}";
        return (negative ? "-" : "") + display + Suffixes[group];
    }
}
