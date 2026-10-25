// Hand-written (not overwritten by the converter): saving and loading Glue3D projects as folders.
//
// In Scratch, a Glue3D project *was* the state of all its variables and lists (saving the .sb3 saved them).
// So a project folder holds exactly that, as JSON, in "project.glue3d":
//
//   Projects/
//     my-game/
//       project.glue3d   <- name, engine, dates, and every variable and list of the project
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Scratch;

namespace Glue3D;

public static class ProjectFiles
{
    public const string FileName = "project.glue3d";
    const string Format = "glue3d-csharp-project";
    const int FormatVersion = 1;

    /// <summary>New projects are created here: a "Projects" folder next to Glue3D.exe.</summary>
    public static string ProjectsRoot
    {
        get
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Projects");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static bool IsProjectFolder(string folder) => File.Exists(Path.Combine(folder, FileName));

    /// <summary>Basic facts about a saved project, read without loading it.</summary>
    public sealed record Info(string Name, string Engine, DateTime Saved);

    public static Info? ReadInfo(string folder)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(folder, FileName));
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            return new Info(
                root.GetProperty("name").GetString() ?? Path.GetFileName(folder),
                root.TryGetProperty("engine", out var e) ? e.GetString() ?? "" : "",
                root.TryGetProperty("saved", out var s) && s.TryGetDateTime(out var d) ? d : DateTime.MinValue);
        }
        catch
        {
            return null;
        }
    }

    // ---------------- save ----------------

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Stage fields are kept by the DynamicallyAccessedMembers annotation.")]
    public static void Save(string folder, string name, string engine,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type stageType, object stage)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, FileName);
        DateTime created = File.Exists(path) && ReadCreated(path) is { } c ? c : DateTime.Now;

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", FormatVersion);
            w.WriteString("name", name);
            w.WriteString("engine", engine);
            w.WriteString("created", created);
            w.WriteString("saved", DateTime.Now);

            w.WriteStartObject("variables");
            foreach (var f in stageType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (f.FieldType == typeof(Value)) { w.WritePropertyName(f.Name); WriteValue(w, (Value)f.GetValue(stage)!); }
            w.WriteEndObject();

            w.WriteStartObject("lists");
            foreach (var f in stageType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                if (f.FieldType == typeof(ScratchList))
                {
                    w.WriteStartArray(f.Name);
                    foreach (var v in ((ScratchList)f.GetValue(stage)!).Items) WriteValue(w, v);
                    w.WriteEndArray();
                }
            w.WriteEndObject();
            w.WriteEndObject();
        }

        // Write to a temporary file first so a crash mid-save never leaves a broken project.
        string temp = path + ".tmp";
        File.WriteAllBytes(temp, buffer.ToArray());
        File.Move(temp, path, overwrite: true);
    }

    static DateTime? ReadCreated(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("created", out var c) && c.TryGetDateTime(out var d) ? d : null;
        }
        catch { return null; }
    }

    static void WriteValue(Utf8JsonWriter w, Value v)
    {
        switch (v.Type)
        {
            case Scratch.ValueType.Boolean: w.WriteBooleanValue(v.Bool); break;
            case Scratch.ValueType.String: w.WriteStringValue(v.Str); break;
            default:
                double n = v.JsNumber;
                if (double.IsFinite(n)) w.WriteNumberValue(n);
                else { w.WriteStartObject(); w.WriteString("number", Js.NumberToString(n)); w.WriteEndObject(); } // NaN / Infinity
                break;
        }
    }

    // ---------------- load ----------------

    /// <summary>Puts every saved variable and list back into the stage. Returns the project's info.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Stage fields are kept by the DynamicallyAccessedMembers annotation.")]
    public static Info Load(string folder,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type stageType, object stage)
    {
        string path = Path.Combine(folder, FileName);
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var root = doc.RootElement;
        if (!root.TryGetProperty("format", out var fmt) || fmt.GetString() != Format)
            throw new InvalidDataException("not a Glue3D project");

        var fields = new Dictionary<string, FieldInfo>();
        foreach (var f in stageType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            fields[f.Name] = f;

        if (root.TryGetProperty("variables", out var vars))
            foreach (var p in vars.EnumerateObject())
                if (fields.TryGetValue(p.Name, out var f) && f.FieldType == typeof(Value))
                    f.SetValue(stage, ReadValue(p.Value));

        if (root.TryGetProperty("lists", out var lists))
            foreach (var p in lists.EnumerateObject())
                if (fields.TryGetValue(p.Name, out var f) && f.FieldType == typeof(ScratchList))
                {
                    var list = (ScratchList)f.GetValue(stage)!;
                    var items = new List<Value>(p.Value.GetArrayLength());
                    foreach (var item in p.Value.EnumerateArray()) items.Add(ReadValue(item));
                    list.Items = items;
                    list.Version++;
                }

        return new Info(
            root.GetProperty("name").GetString() ?? Path.GetFileName(folder),
            root.TryGetProperty("engine", out var e) ? e.GetString() ?? "" : "",
            root.TryGetProperty("saved", out var s) && s.TryGetDateTime(out var d) ? d : DateTime.MinValue);
    }

    static Value ReadValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => (Value)e.GetDouble(),
        JsonValueKind.True => (Value)true,
        JsonValueKind.False => (Value)false,
        JsonValueKind.String => (Value)(e.GetString() ?? ""),
        JsonValueKind.Object when e.TryGetProperty("number", out var n) => (Value)Js.ToNumber(n.GetString() ?? "NaN"),
        _ => Value.Empty,
    };
}
