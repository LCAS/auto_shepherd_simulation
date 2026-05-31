using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    public class CameraController : MonoBehaviour
    {
        [Header("Camera Tracking")]
        [SerializeField] private string sheepTag = "Sheep";
        [SerializeField] private string goalTag = "Goal";
        [SerializeField] private float minCameraHeight = 15f;
        [SerializeField] private float maxCameraHeight = 50f;
        [SerializeField] private float worldEdgeBuffer = 4f;
        [SerializeField, Range(0f, 1f)] private float viewportEdgeBuffer = 0.1f;
        [SerializeField] private float smoothDampTime = 0.3f;
        [SerializeField] private float zoomOutSmoothTime = 0.08f;
        [SerializeField] private bool enableDebugLogs = false;

        private Camera _cam;
        private Vector3 _velocity = Vector3.zero;
        private float _heightVelocity;

        private void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null)
            {
                _cam = Camera.main;
            }

            if (_cam == null)
            {
                Debug.LogWarning("[CameraController] No camera found on this object or as main camera.", this);
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            GameObject[] sheepObjects = GameObject.FindGameObjectsWithTag(sheepTag);
            GameObject goalObj = GameObject.FindGameObjectWithTag(goalTag);

            if (sheepObjects.Length == 0)
            {
                if (enableDebugLogs) Debug.Log("[CameraController] No sheep found.");
                return;
            }

            // Compute bounds containing all sheep and goal
            Bounds bounds = new Bounds(sheepObjects[0].transform.position, Vector3.zero);

            for (int i = 0; i < sheepObjects.Length; i++)
            {
                bounds.Encapsulate(sheepObjects[i].transform.position);
            }

            if (goalObj != null)
            {
                bounds.Encapsulate(goalObj.transform.position);
            }

            // Expand bounds with world-space buffer so there is always space near screen edges.
            Vector3 boundSize = bounds.size;
            boundSize.x += worldEdgeBuffer * 2f;
            boundSize.z += worldEdgeBuffer * 2f;
            bounds.size = boundSize;

            Vector3 boundsCenter = bounds.center;
            Vector3 targetXZ = new Vector3(boundsCenter.x, transform.position.y, boundsCenter.z);
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, targetXZ, ref _velocity, smoothDampTime);

            float desiredHeight = CalculateRequiredHeight(bounds, smoothed);
            float heightSmooth = desiredHeight > transform.position.y ? zoomOutSmoothTime : smoothDampTime;
            float nextY = Mathf.SmoothDamp(transform.position.y, desiredHeight, ref _heightVelocity, heightSmooth);

            Vector3 nextPos = new Vector3(smoothed.x, nextY, smoothed.z);

            // Enforce fit after smoothing: if XZ lag causes clipping risk, increase height immediately.
            float enforcedHeight = CalculateRequiredHeight(bounds, nextPos);
            if (nextPos.y < enforcedHeight)
            {
                nextPos.y = enforcedHeight;
            }

            transform.position = nextPos;

            // Keep looking straight down (top-down orthographic-like view)
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            if (enableDebugLogs)
            {
                Debug.Log($"[CameraController] Pos={transform.position}, DesiredHeight={desiredHeight:F2}, EnforcedHeight={enforcedHeight:F2}, SheepCount={sheepObjects.Length}");
            }
        }

        private float CalculateRequiredHeight(Bounds bounds, Vector3 cameraPos)
        {
            float verticalFovRad = _cam.fieldOfView * Mathf.Deg2Rad;
            float tanHalf = Mathf.Tan(verticalFovRad * 0.5f);
            float safeViewportFactor = Mathf.Clamp01(1f - viewportEdgeBuffer * 2f);
            safeViewportFactor = Mathf.Max(0.05f, safeViewportFactor);

            float offsetX = Mathf.Abs(cameraPos.x - bounds.center.x);
            float offsetZ = Mathf.Abs(cameraPos.z - bounds.center.z);

            float requiredHalfWidth = bounds.extents.x + offsetX;
            float requiredHalfDepth = bounds.extents.z + offsetZ;

            float heightFromDepth = requiredHalfDepth / (tanHalf * safeViewportFactor);
            float heightFromWidth = requiredHalfWidth / (tanHalf * Mathf.Max(0.05f, _cam.aspect) * safeViewportFactor);

            float requiredHeight = Mathf.Max(heightFromDepth, heightFromWidth);
            return Mathf.Clamp(requiredHeight, minCameraHeight, maxCameraHeight);
        }
    }
}
