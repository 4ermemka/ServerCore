using UnityEngine;
namespace ServerCore.World.Presentation
{
    public sealed class PlanLabelBillboard : MonoBehaviour
    {
        private void LateUpdate() { if (Camera.main != null) transform.rotation = Camera.main.transform.rotation; }
    }
}
