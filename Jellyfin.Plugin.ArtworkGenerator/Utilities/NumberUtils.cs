using System;
using System.Globalization;

namespace Jellyfin.Plugin.ArtworkGenerator.Utilities;

public static class NumberUtils
{
    private static readonly string[] UnitsMap = {
        "ZERO", "ONE", "TWO", "THREE", "FOUR", "FIVE", "SIX", "SEVEN", "EIGHT", "NINE",
        "TEN", "ELEVEN", "TWELVE", "THIRTEEN", "FOURTEEN", "FIFTEEN", "SIXTEEN", "SEVENTEEN", "EIGHTEEN", "NINETEEN"
    };

    private static readonly string[] TensMap = {
        "ZERO", "TEN", "TWENTY", "THIRTY", "FORTY", "FIFTY", "SIXTY", "SEVENTY", "EIGHTY", "NINETY"
    };

    // NumberToWords
    // Converts a number to its uppercase English word representation.
    public static string NumberToWords(int number)
    {
        if (number < 0)
            return "MINUS " + NumberToWords(Math.Abs(number));

        if (number < 20)
            return UnitsMap[number];

        if (number < 100)
        {
            string tens = TensMap[number / 10];
            string units = number % 10 > 0 ? " " + UnitsMap[number % 10] : "";
            return tens + units;
        }

        return number.ToString(CultureInfo.InvariantCulture);
    }

    // NumberToRomanNumeral
    // Converts a number to its Roman numeral representation.
    public static string NumberToRomanNumeral(int number)
    {
        if (number <= 0 || number > 3999)
            return number.ToString(CultureInfo.InvariantCulture);

        string[] thousands = { "", "M", "MM", "MMM" };
        string[] hundreds = { "", "C", "CC", "CCC", "CD", "D", "DC", "DCC", "DCCC", "CM" };
        string[] tens = { "", "X", "XX", "XXX", "XL", "L", "LX", "LXX", "LXXX", "XC" };
        string[] ones = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" };

        return thousands[number / 1000] +
               hundreds[(number % 1000) / 100] +
               tens[(number % 100) / 10] +
               ones[number % 10];
    }
}
