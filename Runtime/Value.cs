using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Scratch;

public enum ValueType : byte { Number, String, Boolean }

/// <summary>
/// A Scratch value: a number, a string or a boolean, converted between the three
/// exactly the way Scratch does (Scratch runs on JavaScript, so these follow JS rules).
/// </summary>
public readonly struct Value
{
    public readonly ValueType Type;
    private readonly double _num;
    private readonly string? _str;

    private Value(double n) { Type = ValueType.Number; _num = n; _str = null; }
    private Value(string s) { Type = ValueType.String; _num = 0; _str = s; }
    private Value(bool b) { Type = ValueType.Boolean; _num = b ? 1 : 0; _str = null; }

    public static readonly Value Empty = new("");
    public static readonly Value Zero = new(0.0);

    public static implicit operator Value(double n) => new(n);
    public static implicit operator Value(int n) => new((double)n);
    public static implicit operator Value(string s) => new(s);
    public static implicit operator Value(bool b) => new(b);

    /// <summary>Scratch's "toNumber": anything that isn't a valid number becomes 0.</summary>
    public double Num
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Type switch
        {
            ValueType.Number => double.IsNaN(_num) ? 0 : _num,
            ValueType.Boolean => _num,
            _ => Js.ToNumberOrZero(_str!),
        };
    }

    /// <summary>JavaScript's Number(value): like Num, but invalid text gives NaN.</summary>
    public double JsNumber => Type switch
    {
        ValueType.Number => _num,
        ValueType.Boolean => _num,
        _ => Js.ToNumber(_str!),
    };

    /// <summary>Scratch's "toString".</summary>
    public string Str
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Type switch
        {
            ValueType.String => _str!,
            ValueType.Number => Js.NumberToString(_num),
            _ => _num != 0 ? "true" : "false",
        };
    }

    /// <summary>Scratch's "toBoolean".</summary>
    public bool Bool => Type switch
    {
        ValueType.Boolean => _num != 0,
        ValueType.Number => _num != 0 && !double.IsNaN(_num),
        _ => Js.StringToBool(_str!),
    };

    public bool IsNumber => Type == ValueType.Number;
    public bool IsString => Type == ValueType.String;
    internal double RawNumber => _num;

    /// <summary>Scratch's isWhiteSpace check, used by comparisons.</summary>
    public bool IsWhiteSpace => Type == ValueType.String && Js.IsWhiteSpaceOnly(_str!);

    /// <summary>Scratch's Cast.isInt (used by "pick random").</summary>
    public bool IsInt => Type switch
    {
        ValueType.Number => double.IsNaN(_num) || _num == Math.Truncate(_num),
        ValueType.Boolean => true,
        _ => _str!.IndexOf('.') < 0,
    };

    public override string ToString() => Str;

    /// <summary>
    /// Text that round-trips exactly through a number ("12", "-0.5") is stored as a number.
    /// That is unobservable in Scratch but saves re-parsing text on every use.
    /// </summary>
    public static Value FromLiteral(string s)
    {
        double n = Js.ToNumber(s);
        if (!double.IsNaN(n) && Js.NumberToString(n) == s) return new Value(n);
        return new Value(s);
    }
}

/// <summary>JavaScript number/string conversion rules.</summary>
public static class Js
{
    static bool IsJsSpace(char c) =>
        c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '\f' ||
        c == (char)0x00A0 || c == (char)0xFEFF || c == (char)0x1680 || (c >= (char)0x2000 && c <= (char)0x200A) ||
        c == (char)0x2028 || c == (char)0x2029 || c == (char)0x202F || c == (char)0x205F || c == (char)0x3000;

    public static bool IsWhiteSpaceOnly(string s)
    {
        foreach (char c in s) if (!IsJsSpace(c)) return false;
        return true;
    }

    static string JsTrim(string s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsJsSpace(s[a])) a++;
        while (b > a && IsJsSpace(s[b - 1])) b--;
        return (a == 0 && b == s.Length) ? s : s.Substring(a, b - a);
    }

    public static bool StringToBool(string s) =>
        !(s.Length == 0 || s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToNumberOrZero(string s)
    {
        double n = ToNumber(s);
        return double.IsNaN(n) ? 0 : n;
    }

    /// <summary>JavaScript Number(string).</summary>
    public static double ToNumber(string s)
    {
        // Fast path: short plain integers.
        int len = s.Length;
        if (len > 0 && len < 16)
        {
            int i = 0; bool neg = false;
            if (s[0] == '-') { neg = true; i = 1; }
            if (i < len)
            {
                long v = 0; bool ok = true;
                for (int k = i; k < len; k++)
                {
                    int d = s[k] - '0';
                    if ((uint)d > 9) { ok = false; break; }
                    v = v * 10 + d;
                }
                if (ok) return neg ? (v == 0 ? -0.0 : -v) : v;
            }
        }
        string t = JsTrim(s);
        if (t.Length == 0) return 0;
        if (t == "Infinity" || t == "+Infinity") return double.PositiveInfinity;
        if (t == "-Infinity") return double.NegativeInfinity;
        if (t.Length > 2 && t[0] == '0')
        {
            char p = t[1];
            int radix = p is 'x' or 'X' ? 16 : p is 'o' or 'O' ? 8 : p is 'b' or 'B' ? 2 : 0;
            if (radix != 0)
            {
                double v = 0;
                for (int k = 2; k < t.Length; k++)
                {
                    int d = HexDigit(t[k]);
                    if (d < 0 || d >= radix) return double.NaN;
                    v = v * radix + d;
                }
                return v;
            }
        }
        // StrDecimalLiteral: [+-] (digits [. digits] | . digits) [e [+-] digits]
        int j = 0, n = t.Length;
        if (t[j] == '+' || t[j] == '-') j++;
        int intDigits = 0, fracDigits = 0;
        while (j < n && char.IsAsciiDigit(t[j])) { j++; intDigits++; }
        if (j < n && t[j] == '.') { j++; while (j < n && char.IsAsciiDigit(t[j])) { j++; fracDigits++; } }
        if (intDigits + fracDigits == 0) return double.NaN;
        if (j < n && (t[j] == 'e' || t[j] == 'E'))
        {
            j++;
            if (j < n && (t[j] == '+' || t[j] == '-')) j++;
            int expDigits = 0;
            while (j < n && char.IsAsciiDigit(t[j])) { j++; expDigits++; }
            if (expDigits == 0) return double.NaN;
        }
        if (j != n) return double.NaN;
        return double.Parse(t, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    static int HexDigit(char c) =>
        c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;

    /// <summary>JavaScript Number.prototype.toString() (shortest round-trip digits, JS exponent rules).</summary>
    public static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";
        if (d == Math.Truncate(d) && Math.Abs(d) < 1e15) return ((long)d).ToString(CultureInfo.InvariantCulture);

        // "E16" gives 17 significant digits; "R" gives the shortest round-trip form in .NET Core 3+.
        string r = d.ToString("R", CultureInfo.InvariantCulture);
        bool neg = r[0] == '-';
        if (neg) r = r.Substring(1);
        // Split into digits and exponent: value = 0.DIGITS * 10^n
        string mant = r; int exp = 0;
        int e = r.IndexOfAny(new[] { 'E', 'e' });
        if (e >= 0) { mant = r.Substring(0, e); exp = int.Parse(r.Substring(e + 1), CultureInfo.InvariantCulture); }
        int dot = mant.IndexOf('.');
        string digits; int pointPos;
        if (dot >= 0) { digits = mant.Remove(dot, 1); pointPos = dot; }
        else { digits = mant; pointPos = mant.Length; }
        int lead = 0;
        while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; pointPos--; }
        digits = digits.Substring(lead).TrimEnd('0');
        if (digits.Length == 0) digits = "0";
        int k = digits.Length;
        int nExp = pointPos + exp; // decimal point position relative to digits start

        string body;
        if (k <= nExp && nExp <= 21) body = digits + new string('0', nExp - k);
        else if (0 < nExp && nExp <= 21) body = digits.Substring(0, nExp) + "." + digits.Substring(nExp);
        else if (-6 < nExp && nExp <= 0) body = "0." + new string('0', -nExp) + digits;
        else
        {
            int ee = nExp - 1;
            string es = (ee >= 0 ? "+" : "-") + Math.Abs(ee).ToString(CultureInfo.InvariantCulture);
            body = k == 1 ? digits + "e" + es : digits[0] + "." + digits.Substring(1) + "e" + es;
        }
        return neg ? "-" + body : body;
    }
}
