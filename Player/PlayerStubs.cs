// The editor's hand-written features (start menu, saving, build dialog, scene gizmo) aren't part of a game.
// These empty stand-ins let the shared Glue3D code compile without them.
namespace Glue3D;

public sealed partial class Glue3DSprite
{
    const bool MainMenuActive = false;
    void MainMenuFrame() { }
    void SaveProject() { }
    void OpenBuildDialog() { }
    void BuildDialogFrame() { }
    bool SceneViewActive() => false;
    bool InSceneViewport(double x, double y) => false;
}
