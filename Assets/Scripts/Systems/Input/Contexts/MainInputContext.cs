public class MainInputContext : InputContext
{
    private readonly CameraManager cameraManager;
    public MainInputContext()
    {
        cameraManager = ManagersHolder.Instance.GetManager<CameraManager>();
        Actions.Add(new InputAction("Camera.RotateLeft", () => cameraManager.Orbit(1)));
        Actions.Add(new InputAction("Camera.RotateRight", () => cameraManager.Orbit(-1)));
    }
}
