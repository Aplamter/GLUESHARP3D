using System;
using System.Collections.Generic;
using System.Linq;

namespace Scratch;

/// <summary>A Scratch list, with Scratch's 1-based index rules.</summary>
public sealed class ScratchList
{
    public readonly string Name;
    public List<Value> Items;
    /// <summary>Bumped on every change so list monitors know when to redraw.</summary>
    public int Version;

    public ScratchList(string name, IEnumerable<Value>? items = null)
    {
        Name = name;
        Items = items != null ? new List<Value>(items) : new List<Value>();
    }

    public ScratchList Copy() => new(Name, Items);

    const int Invalid = -1, All = -2;

    /// <summary>Scratch's Cast.toListIndex: returns a 1-based index, or Invalid/All.</summary>
    int ToIndex(Value index, bool acceptAll)
    {
        int length = Items.Count;
        if (!index.IsNumber)
        {
            string s = index.Str;
            if (s == "all") return acceptAll ? All : Invalid;
            if (s == "last") return length > 0 ? length : Invalid;
            if (s == "random" || s == "any") return length > 0 ? 1 + (int)Math.Floor(Ops.RandomUnit() * length) : Invalid;
        }
        double i = Math.Floor(index.Num);
        if (i < 1 || i > length) return Invalid;
        return (int)i;
    }

    public Value Item(Value index)
    {
        int i = ToIndex(index, false);
        return i == Invalid ? Value.Empty : Items[i - 1];
    }

    /// <summary>Fast path for numeric indexes.</summary>
    public Value Item(double index)
    {
        if (double.IsNaN(index)) index = 0;
        double i = Math.Floor(index);
        return (i >= 1 && i <= Items.Count) ? Items[(int)i - 1] : Value.Empty;
    }

    public int Length => Items.Count;

    public void Add(Value v) { Items.Add(v); Version++; }

    public void Delete(Value index)
    {
        int i = ToIndex(index, true);
        if (i == Invalid) return;
        if (i == All) Items.Clear();
        else Items.RemoveAt(i - 1);
        Version++;
    }

    public void DeleteAll() { Items.Clear(); Version++; }

    public void Insert(Value index, Value item)
    {
        int length = Items.Count;
        int i;
        if (!index.IsNumber && index.Str == "last") i = length + 1;
        else if (!index.IsNumber && (index.Str == "random" || index.Str == "any")) i = 1 + (int)Math.Floor(Ops.RandomUnit() * (length + 1));
        else
        {
            double d = Math.Floor(index.Num);
            if (d < 1 || d > length + 1) return;
            i = (int)d;
        }
        Items.Insert(i - 1, item);
        Version++;
    }

    public void Replace(Value index, Value item)
    {
        int i = ToIndex(index, false);
        if (i == Invalid) return;
        Items[i - 1] = item;
        Version++;
    }

    /// <summary>"item # of": 1-based position or 0.</summary>
    public double IndexOf(Value item)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Ops.Compare(Items[i], item) == 0) return i + 1;
        return 0;
    }

    public bool Contains(Value item)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Ops.Compare(Items[i], item) == 0) return true;
        return false;
    }

    /// <summary>The list reporter: items joined by spaces, or with nothing if all are single characters.</summary>
    public string Contents()
    {
        bool allSingle = Items.All(v => v.Str.Length == 1);
        return string.Join(allSingle ? "" : " ", Items.Select(v => v.Str));
    }
}
