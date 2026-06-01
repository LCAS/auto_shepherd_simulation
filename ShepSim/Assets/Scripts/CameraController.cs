using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;

namespace Ursaanimation.CubicFarmAnimals
{
    public class CameraController : MonoBehaviour
    {
        [Header("Camera Tracking")]
        [SerializeField] private string sheepTag = "Sheep";
        [SerializeField] private string dogTag = "Dog";
        [SerializeField] private string goalTag = "Goal";
        [SerializeField] private float minCameraHeight = 15f;
        [SerializeField] private float maxCameraHeight = 50f;
        [SerializeField] private float worldEdgeBuffer = 4f;
        [SerializeField, Range(0f, 1f)] private float viewportEdgeBuffer = 0.1f;
        [SerializeField] private float smoothDampTime = 0.3f;
        [SerializeField] private float zoomOutSmoothTime = 0.08f;
        [SerializeField] private bool enableDebugLogs = false;
        [Header("Image Capture")]
        [SerializeField] private KeyCode captureKey = KeyCode.Space;
        [SerializeField] private string captureFolderName = "Captures";
        [Header("Ground Truth Capture")]
        [SerializeField] private Camera groundTruthCamera;
        [SerializeField] private LayerMask groundTruthCullingMask = ~0;
        [SerializeField] private Material groundTruthSheepMaterial;
        [SerializeField] private Material groundTruthDogMaterial;
        [SerializeField] private Color groundTruthBackgroundColor = Color.black;
        [Header("Raw Capture")]
        [SerializeField] private Camera rawCamera;
        [SerializeField] private LayerMask rawNoDebugCullingMask = ~0;
        [SerializeField] private string rgbSuffix = "rgb";
        [SerializeField] private string rawSuffix = "raw";
        [SerializeField] private string gtSuffix = "gt";

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

            if (groundTruthCamera == null)
            {
                groundTruthCamera = _cam;
            }

            if (rawCamera == null)
            {
                rawCamera = _cam;
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;

            if (Input.GetKeyDown(captureKey))
            {
                CaptureRgbAndGroundTruthPair();
            }

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

        private void CaptureRgbAndGroundTruthPair()
        {
            string folderPath = Path.Combine(Application.persistentDataPath, captureFolderName);
            Directory.CreateDirectory(folderPath);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string rgbPath = Path.Combine(folderPath, $"{stamp}_{rgbSuffix}.png");
            string rawPath = Path.Combine(folderPath, $"{stamp}_{rawSuffix}.png");
            string gtPath = Path.Combine(folderPath, $"{stamp}_{gtSuffix}.png");

            try
            {
                CaptureCameraToFile(_cam, rgbPath);
                CaptureCameraWithCullingMaskToFile(rawCamera, rawNoDebugCullingMask, rawPath);
                CaptureGroundTruthToFile(gtPath);
                Debug.Log($"[CameraController] Captured image set:\nRGB: {rgbPath}\nRAW: {rawPath}\nGT: {gtPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CameraController] Failed to capture image set: {ex.Message}", this);
            }
        }

        private void CaptureGroundTruthToFile(string filePath)
        {
            if (groundTruthCamera == null)
            {
                throw new InvalidOperationException("Ground truth camera is not assigned.");
            }

            if (groundTruthSheepMaterial == null)
            {
                throw new InvalidOperationException("Ground truth sheep material is not assigned.");
            }

            int originalCullingMask = groundTruthCamera.cullingMask;
            CameraClearFlags originalClearFlags = groundTruthCamera.clearFlags;
            Color originalBackgroundColor = groundTruthCamera.backgroundColor;
            Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
            List<Material> temporaryMaterials = new List<Material>();
            List<Renderer> temporarilyDisabledRenderers = new List<Renderer>();

            try
            {
                // Exclude debug visual layers in GT output using camera culling mask.
                groundTruthCamera.cullingMask = groundTruthCullingMask;
                groundTruthCamera.clearFlags = CameraClearFlags.SolidColor;
                groundTruthCamera.backgroundColor = groundTruthBackgroundColor;

                HashSet<Renderer> targetRenderers = CollectTargetRenderers();
                DisableNonTargetRenderers(targetRenderers, temporarilyDisabledRenderers);

                ApplyUniqueSheepOverrides(originalMaterials, temporaryMaterials);

                if (groundTruthDogMaterial != null)
                {
                    ApplyMaterialOverrideToTaggedObjects(dogTag, groundTruthDogMaterial, originalMaterials);
                }
                else if (enableDebugLogs)
                {
                    Debug.Log("[CameraController] Ground truth dog material is not assigned. Dog objects will keep their original materials.");
                }

                CaptureCameraToFile(groundTruthCamera, filePath);
            }
            finally
            {
                groundTruthCamera.cullingMask = originalCullingMask;
                groundTruthCamera.clearFlags = originalClearFlags;
                groundTruthCamera.backgroundColor = originalBackgroundColor;

                foreach (KeyValuePair<Renderer, Material[]> kvp in originalMaterials)
                {
                    if (kvp.Key != null)
                    {
                        kvp.Key.sharedMaterials = kvp.Value;
                    }
                }

                for (int i = 0; i < temporaryMaterials.Count; i++)
                {
                    if (temporaryMaterials[i] != null)
                    {
                        Destroy(temporaryMaterials[i]);
                    }
                }

                for (int i = 0; i < temporarilyDisabledRenderers.Count; i++)
                {
                    if (temporarilyDisabledRenderers[i] != null)
                    {
                        temporarilyDisabledRenderers[i].enabled = true;
                    }
                }
            }
        }

        private HashSet<Renderer> CollectTargetRenderers()
        {
            HashSet<Renderer> targetRenderers = new HashSet<Renderer>();
            AddTaggedRenderersToSet(sheepTag, targetRenderers);
            AddTaggedRenderersToSet(dogTag, targetRenderers);
            return targetRenderers;
        }

        private void AddTaggedRenderersToSet(string targetTag, HashSet<Renderer> targetRenderers)
        {
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(targetTag);
            for (int i = 0; i < taggedObjects.Length; i++)
            {
                Renderer[] renderers = taggedObjects[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    if (renderers[r] != null)
                    {
                        targetRenderers.Add(renderers[r]);
                    }
                }
            }
        }

        private void DisableNonTargetRenderers(HashSet<Renderer> targetRenderers, List<Renderer> temporarilyDisabledRenderers)
        {
            Renderer[] allRenderers = FindObjectsOfType<Renderer>(true);
            for (int i = 0; i < allRenderers.Length; i++)
            {
                Renderer renderer = allRenderers[i];
                if (renderer == null || !renderer.enabled || targetRenderers.Contains(renderer))
                {
                    continue;
                }

                renderer.enabled = false;
                temporarilyDisabledRenderers.Add(renderer);
            }
        }

        private void ApplyUniqueSheepOverrides(Dictionary<Renderer, Material[]> originalMaterials, List<Material> temporaryMaterials)
        {
            GameObject[] sheepObjects = GameObject.FindGameObjectsWithTag(sheepTag);
            for (int i = 0; i < sheepObjects.Length; i++)
            {
                Material sheepMaterial = new Material(groundTruthSheepMaterial);
                SetMaterialColor(sheepMaterial, GenerateUniqueSheepColor(i));
                temporaryMaterials.Add(sheepMaterial);

                Renderer[] renderers = sheepObjects[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || originalMaterials.ContainsKey(renderer))
                    {
                        continue;
                    }

                    Material[] shared = renderer.sharedMaterials;
                    originalMaterials[renderer] = shared;

                    Material[] overridden = new Material[shared.Length];
                    for (int m = 0; m < overridden.Length; m++)
                    {
                        overridden[m] = sheepMaterial;
                    }

                    renderer.sharedMaterials = overridden;
                }
            }
        }

        private Color GenerateUniqueSheepColor(int sheepIndex)
        {
            const float goldenRatio = 0.61803398875f;
            float hue = Mathf.Repeat(sheepIndex * goldenRatio, 1f);
            return Color.HSVToRGB(hue, 1f, 1f);
        }

        private void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        private void ApplyMaterialOverrideToTaggedObjects(string targetTag, Material overrideMaterial, Dictionary<Renderer, Material[]> originalMaterials)
        {
            GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(targetTag);
            for (int i = 0; i < taggedObjects.Length; i++)
            {
                Renderer[] renderers = taggedObjects[i].GetComponentsInChildren<Renderer>(true);
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer == null || originalMaterials.ContainsKey(renderer))
                    {
                        continue;
                    }

                    Material[] shared = renderer.sharedMaterials;
                    originalMaterials[renderer] = shared;

                    Material[] overridden = new Material[shared.Length];
                    for (int m = 0; m < overridden.Length; m++)
                    {
                        overridden[m] = overrideMaterial;
                    }

                    renderer.sharedMaterials = overridden;
                }
            }
        }

        private void CaptureCameraToFile(Camera sourceCamera, string filePath)
        {
            if (sourceCamera == null)
            {
                throw new InvalidOperationException("Source camera is null.");
            }

            int width = Screen.width;
            int height = Screen.height;

            RenderTexture renderTexture = new RenderTexture(width, height, 24);
            Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);

            RenderTexture previousTarget = sourceCamera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                sourceCamera.targetTexture = renderTexture;
                sourceCamera.Render();

                RenderTexture.active = renderTexture;
                screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                screenshot.Apply();

                File.WriteAllBytes(filePath, screenshot.EncodeToPNG());
            }
            finally
            {
                sourceCamera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;

                Destroy(renderTexture);
                Destroy(screenshot);
            }
        }

        private void CaptureCameraWithCullingMaskToFile(Camera sourceCamera, LayerMask cullingMask, string filePath)
        {
            if (sourceCamera == null)
            {
                throw new InvalidOperationException("Source camera is null.");
            }

            int originalCullingMask = sourceCamera.cullingMask;
            try
            {
                sourceCamera.cullingMask = cullingMask;
                CaptureCameraToFile(sourceCamera, filePath);
            }
            finally
            {
                sourceCamera.cullingMask = originalCullingMask;
            }
        }
    }
}
