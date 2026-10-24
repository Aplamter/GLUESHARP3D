using System;
using System.IO;
using System.Linq;
using Scratch;

namespace Glue3D;

public static class Program
{
    public static void Main()
    {
        string assetDir = Path.Combine(AppContext.BaseDirectory, "assets");
        var engine = new Engine();
        engine.Run(Project.Title, e =>
        {
            var manifest = AssetManifest.Load(assetDir);
            Project.Create(e, manifest, assetDir, new InitialLists(assetDir));

            // Hand-written editor additions (Custom/): scene toolbar + gizmos.
            var glue = e.Sprites.OfType<Glue3DSprite>().First();
            e.BeforeScripts += glue.SceneGizmoUpdate;
            e.Overlay += glue.SceneGizmoDraw;
            Automation.Hooks["addobject"] = arg =>
            {
                var a = arg.Split(' ');
                glue.AddTestObject(a[0], double.Parse(a[1]), double.Parse(a[2]), double.Parse(a[3]));
            };
            Automation.Hooks["camera"] = arg =>
            {
                var a = Array.ConvertAll(arg.Split(' '), double.Parse);
                glue.SetCamera(a[0], a[1], a[2], a[3], a[4]);
            };
            Automation.Hooks["where"] = name => Console.WriteLine("[where] " + glue.DescribeObject(name));
        });
    }
}
