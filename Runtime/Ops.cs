using System;
using System.Runtime.CompilerServices;

namespace Scratch;

/// <summary>Scratch's operator blocks. Generated code uses these via "using static Scratch.Ops".</summary>
public static class Ops
{
    // ---- conversions used by generated code ----
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Num(string s) => Js.ToNumberOrZero(s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Str(double n) => Js.NumberToString(n);
    public static string Str(bool b) => b ? "true" : "false";
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Bool(double n) => n != 0 && !double.IsNaN(n);
    public static bool Bool(string s) => Js.StringToBool(s);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Num(bool b) => b ? 1 : 0;
    /// <summary>Scratch turns NaN into 0 wherever a block reads a number.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Nz(double n) => double.IsNaN(n) ? 0 : n;

    // ---- comparison (Scratch's Cast.compare) ----
    public static double Compare(Value a, Value b)
    {
        double n1 = a.JsNumber, n2 = b.JsNumber;
        if (n1 == 0 && a.IsWhiteSpace) n1 = double.NaN;
        else if (n2 == 0 && b.IsWhiteSpace) n2 = double.NaN;
        if (double.IsNaN(n1) || double.IsNaN(n2))
        {
            int c = string.CompareOrdinal(a.Str.ToLowerInvariant(), b.Str.ToLowerInvariant());
            return c < 0 ? -1 : c > 0 ? 1 : 0;
        }
        if ((double.IsPositiveInfinity(n1) && double.IsPositiveInfinity(n2)) ||
            (double.IsNegativeInfinity(n1) && double.IsNegativeInfinity(n2))) return 0;
        return n1 - n2;
    }

    // Fast paths when both sides are known numbers (NaN still follows Scratch's text rules).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Eq(double a, double b) => a == b || ((double.IsNaN(a) || double.IsNaN(b)) && Compare(a, b) == 0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Lt(double a, double b) => (double.IsNaN(a) || double.IsNaN(b)) ? Compare(a, b) < 0 : a < b;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Gt(double a, double b) => (double.IsNaN(a) || double.IsNaN(b)) ? Compare(a, b) > 0 : a > b;

    public static bool Eq(Value a, Value b)
    {
        if (a.IsNumber && b.IsNumber) return Eq(a.RawNumber, b.RawNumber);
        return Compare(a, b) == 0;
    }
    public static bool Lt(Value a, Value b) => (a.IsNumber && b.IsNumber) ? Lt(a.RawNumber, b.RawNumber) : Compare(a, b) < 0;
    public static bool Gt(Value a, Value b) => (a.IsNumber && b.IsNumber) ? Gt(a.RawNumber, b.RawNumber) : Compare(a, b) > 0;

    // ---- math ----
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Mod(double n, double m)
    {
        double r = n % m;
        return r / m < 0 ? r + m : r;
    }

    /// <summary>JavaScript Math.round (halves round up).</summary>
    public static double Round(double x)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return x;
        double f = Math.Floor(x);
        return x - f >= 0.5 ? f + 1 : f;
    }

    public static double Sin(double deg) => Round(Math.Sin(Math.PI * deg / 180) * 1e10) / 1e10;
    public static double Cos(double deg) => Round(Math.Cos(Math.PI * deg / 180) * 1e10) / 1e10;
    public static double Tan(double deg)
    {
        double a = deg % 360;
        if (a == -270 || a == 90) return double.PositiveInfinity;
        if (a == -90 || a == 270) return double.NegativeInfinity;
        return Round(Math.Tan(Math.PI * a / 180) * 1e10) / 1e10;
    }
    public static double Asin(double n) => Math.Asin(n) * 180 / Math.PI;
    public static double Acos(double n) => Math.Acos(n) * 180 / Math.PI;
    public static double Atan(double n) => Math.Atan(n) * 180 / Math.PI;
    public static double Ln(double n) => Math.Log(n);
    public static double Log10(double n) => Math.Log(n) / Math.Log(10);
    public static double Exp(double n) => Math.Exp(n);
    public static double Pow10(double n) => Math.Pow(10, n);

    static readonly Random Rng = new();
    public static double PickRandom(Value from, Value to)
    {
        double a = from.Num, b = to.Num;
        double low = Math.Min(a, b), high = Math.Max(a, b);
        if (low == high) return low;
        if (from.IsInt && to.IsInt) return low + Math.Floor(Rng.NextDouble() * (high + 1 - low));
        return Rng.NextDouble() * (high - low) + low;
    }
    public static double RandomUnit() => Rng.NextDouble();

    // ---- text ----
    public static string LetterOf(double letter, string s)
    {
        double i = letter - 1;
        if (!(i >= 0) || i >= s.Length) return "";
        return s[(int)i].ToString();
    }
    public static bool Contains(string a, string b) =>
        a.ToLowerInvariant().Contains(b.ToLowerInvariant(), StringComparison.Ordinal);

    // ---- helpers for Scratch's "wrapClamp" and "clamp" ----
    public static double Clamp(double n, double min, double max) => Math.Max(min, Math.Min(n, max));
    public static double WrapClamp(double n, double min, double max)
    {
        double range = max - min + 1;
        return n - Math.Floor((n - min) / range) * range;
    }
}
