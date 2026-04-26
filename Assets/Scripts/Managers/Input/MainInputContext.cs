using UnityEngine;

public class MainInputContext : InputContext
{
    protected CameraManager _cameraManager;

    public MainInputContext() : base()
    {
        _cameraManager = ManagersHolder.Instance.GetManager<CameraManager>();

        Actions.Add(new InputAction(name: "Q", onPressed: RotateCameraLeft));
        Actions.Add(new InputAction(name: "E", onPressed: RotateCameraRight));
        Actions.Add(new InputAction(name: "ScrollUp", onPressed: ZoomIn, onHeld: ZoomIn));
        Actions.Add(new InputAction(name: "ScrollDown", onPressed: ZoomOut, onHeld: ZoomOut));
    }

    protected void RotateCameraLeft()
    {
        //Debug.Log("Rotate L");
        _cameraManager.RotateLeft(45);
    }

    protected void RotateCameraRight()
    { 
        //Debug.Log("Rotate R");
        _cameraManager.RotateRight(45);
    }

    protected void ZoomIn()
    {
        _cameraManager.ZoomIn();
    }

    protected void ZoomOut()
    {
        _cameraManager.ZoomOut();
    }
}
