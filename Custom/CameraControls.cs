// Hand-written additions to the generated Glue3DSprite (not overwritten by the converter):
// Unity-style camera controls for the 3D scene view.
using System;
using Scratch;
using static Scratch.Ops;

namespace Glue3D;

public sealed partial class Glue3DSprite
{
    // ---------------- settings ----------------

    /// <summary>Degrees the camera turns per stage unit of mouse movement while right-dragging (the stage is 480 units wide).</summary>
    public const double MouseLookSensitivity = 0.5;
    /// <summary>Set to true to make dragging up look down.</summary>
    public const bool InvertMouseLookY = false;
    /// <summary>How far the camera can look up/down. 90 = straight up/down, like Unity (the original was 45).</summary>
    public const double MaxCameraPitch = 90;
    /// <summary>How far one mouse-wheel notch moves the camera forward/back.</summary>
    public const double WheelZoomStep = 0.5;
    /// <summary>How far the camera slides per stage unit of middle-button drag.</summary>
    public const double PanSpeed = 0.02;

    // ---------------- right-mouse look ----------------

    /// <summary>Horizontal camera turn this frame from right-mouse dragging.</summary>
    double MouseLookX() => RightMouseDown ? LookDeltaX * MouseLookSensitivity : 0;

    /// <summary>Vertical camera turn this frame from right-mouse dragging.</summary>
    double MouseLookY() => RightMouseDown ? LookDeltaY * MouseLookSensitivity * (InvertMouseLookY ? -1 : 1) : 0;

    // ---------------- wheel zoom, middle-button pan, fly up/down ----------------

    /// <summary>
    /// Called at the end of the camera block every frame. Adds what Unity's scene view does:
    /// mouse wheel moves forward/back along the view, middle-drag slides the camera,
    /// and while the right button is held W/S also follow the up/down angle and Q/E move down/up.
    /// </summary>
    void UnityStyleCameraExtras(Value speed)
    {
        double yaw = stage.ramEngineData.Item(118).Num;
        double pitch = stage.ramEngineData.Item(120).Num;
        double camX = stage.ramEngineData.Item(112).Num;
        double camY = stage.ramEngineData.Item(114).Num;
        double camZ = stage.ramEngineData.Item(116).Num;
        double s = speed.Num;

        // Forward along the view direction, including looking up/down.
        double fx = Sin(yaw) * Cos(pitch), fy = Sin(pitch), fz = Cos(yaw) * Cos(pitch);
        // Right of the view (same direction the "d" key moves).
        double rx = Sin(90 + yaw), rz = Cos(90 + yaw);

        double wheel = Engine.Input.WheelMove;
        if (wheel != 0 && SceneViewActive() && InSceneViewport(MouseX, MouseY))
        {
            camX += fx * wheel * WheelZoomStep;
            camY += fy * wheel * WheelZoomStep;
            camZ += fz * wheel * WheelZoomStep;
        }

        if (Engine.Input.MiddleMouseDown)
        {
            // Drag the scene with the mouse: the camera moves the opposite way.
            camX -= rx * Engine.Input.PanDeltaX * PanSpeed;
            camZ -= rz * Engine.Input.PanDeltaX * PanSpeed;
            camY -= Engine.Input.PanDeltaY * PanSpeed;
        }

        if (RightMouseDown)
        {
            // W/S already move horizontally; add the up/down part of the view direction.
            camY += fy * (Num(KeyPressed("w")) - Num(KeyPressed("s"))) * s;
            camY += (Num(KeyPressed("e")) - Num(KeyPressed("q"))) * s;
        }

        if (camY < -0.25) camY = -0.25; // same floor limit as the original camera
        CreateAddress("engine.camera_x", camX, 56);
        CreateAddress("engine.camera_y", camY, 57);
        CreateAddress("engine.camera_z", camZ, 58);
    }
}
