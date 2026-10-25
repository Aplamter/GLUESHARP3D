// Hand-written: which program is being compiled.
//   Glue3D.csproj             -> the editor (IsEditor = true)
//   Player/Glue3DPlayer.csproj -> the game player made by File > build.. (IsEditor = false)
// Because IsEditor is a constant, the C# compiler drops every "if (BuildFlavor.IsEditor ...)" branch in the
// player, and the trimmer then removes all editor code that nothing calls anymore. The player never contains the editor.
namespace Glue3D;

public static class BuildFlavor
{
#if GLUE3D_PLAYER
    public const bool IsEditor = false;
#else
    public const bool IsEditor = true;
#endif
}
