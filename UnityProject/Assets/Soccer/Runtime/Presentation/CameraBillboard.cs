using UnityEngine;

namespace Soccer.Runtime
{
    /// <summary>Keep in-world bars and text readable after the arena camera orbits.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class CameraBillboard : MonoBehaviour
    {
        public Vector3 Anchor;
        public Vector2 Offset;
        private Camera view;
        private void LateUpdate()
        {
            if(view==null)view=Camera.main;
            if(view==null)return;
            transform.rotation=view.transform.rotation;
            transform.position=transform.parent.TransformPoint(Anchor)+view.transform.right*Offset.x+view.transform.up*Offset.y;
        }
    }
}
