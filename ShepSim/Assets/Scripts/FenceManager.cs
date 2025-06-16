using UnityEngine;
using System.Collections.Generic;

public class FenceManager : MonoBehaviour
{
    [System.Serializable]
    public class FencePose
    {
        public Vector3 position;
        public Quaternion rotation;
        public string fenceType;
    }

    public List<FencePose> fencePoses = new List<FencePose>();
    public GameObject fenceType1Prefab;
    public GameObject fenceType2Prefab;

    private void Start()
    {
        // Initialize fence poses from the scene
        InitializeFencePoses();
    }

    private void InitializeFencePoses()
    {
        // Find all fence objects in the scene
        GameObject[] fences = GameObject.FindGameObjectsWithTag("Fence");
        
        foreach (GameObject fence in fences)
        {
            FencePose pose = new FencePose
            {
                position = fence.transform.position,
                rotation = fence.transform.rotation,
                fenceType = fence.name.Contains("Type1") ? "Type1" : "Type2"
            };
            
            fencePoses.Add(pose);
        }
    }

    public void SpawnFence(FencePose pose)
    {
        GameObject prefab = pose.fenceType == "Type1" ? fenceType1Prefab : fenceType2Prefab;
        if (prefab != null)
        {
            GameObject fence = Instantiate(prefab, pose.position, pose.rotation);
            fence.tag = "Fence";
        }
    }

    public void SpawnAllFences()
    {
        foreach (FencePose pose in fencePoses)
        {
            SpawnFence(pose);
        }
    }

    public void ClearAllFences()
    {
        GameObject[] fences = GameObject.FindGameObjectsWithTag("Fence");
        foreach (GameObject fence in fences)
        {
            Destroy(fence);
        }
    }

    public void SaveFencePoses()
    {
        // Save fence poses to a JSON file
        string json = JsonUtility.ToJson(new FencePoseList { poses = fencePoses }, true);
        System.IO.File.WriteAllText(Application.persistentDataPath + "/fence_poses.json", json);
    }

    public void LoadFencePoses()
    {
        // Load fence poses from JSON file
        string path = Application.persistentDataPath + "/fence_poses.json";
        if (System.IO.File.Exists(path))
        {
            string json = System.IO.File.ReadAllText(path);
            FencePoseList poseList = JsonUtility.FromJson<FencePoseList>(json);
            fencePoses = poseList.poses;
        }
    }

    [System.Serializable]
    private class FencePoseList
    {
        public List<FencePose> poses;
    }
} 