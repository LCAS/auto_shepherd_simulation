using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using System.Collections.Generic;

public class RosInterface : MonoBehaviour
{
    private RosSocket rosSocket;
    private string rosBridgeServerUrl = "ws://localhost:9090";
    private string dronePoseTopic = "/drone/pose";
    private string dogPoseTopic = "/dog/pose";
    private string sheepPosesTopic = "/sheep/poses";
    private string dogCommandTopic = "/dog/command";

    private void Start()
    {
        rosSocket = new RosSocket(rosBridgeServerUrl);
        rosSocket.Subscribe<Float64MultiArray>(dogCommandTopic, DogCommandCallback);
    }

    private void Update()
    {
        // Publish drone pose (example: using transform.position)
        Vector3 dronePosition = transform.position;
        Point dronePose = new Point(dronePosition.x, dronePosition.y, dronePosition.z);
        rosSocket.Publish(dronePoseTopic, dronePose);

        // Publish dog pose (example: using a separate GameObject for the dog)
        GameObject dog = GameObject.Find("Dog");
        if (dog != null)
        {
            Vector3 dogPosition = dog.transform.position;
            Quaternion dogRotation = dog.transform.rotation;
            Point dogPose = new Point(dogPosition.x, dogPosition.y, 0);
            Quaternion dogOrientation = new Quaternion(dogRotation.x, dogRotation.y, dogRotation.z, dogRotation.w);
            rosSocket.Publish(dogPoseTopic, dogPose);
        }

        // Publish sheep poses (example: using a list of sheep GameObjects)
        List<GameObject> sheepList = new List<GameObject>(GameObject.FindGameObjectsWithTag("Sheep"));
        List<Point> sheepPoses = new List<Point>();
        foreach (GameObject sheep in sheepList)
        {
            Vector3 sheepPosition = sheep.transform.position;
            sheepPoses.Add(new Point(sheepPosition.x, sheepPosition.y, 0));
        }
        rosSocket.Publish(sheepPosesTopic, sheepPoses);
    }

    private void DogCommandCallback(Float64MultiArray command)
    {
        // Handle incoming dog command
        Debug.Log("Received dog command: " + command);
    }

    private void OnDestroy()
    {
        if (rosSocket != null)
        {
            rosSocket.Close();
        }
    }
} 