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

    // Unity to ROS coordinate system conversion
    private UnityEngine.Vector3 UnityToRosPosition(UnityEngine.Vector3 unityPosition)
    {
        // Convert Unity's right-handed to ROS's left-handed coordinate system
        return new UnityEngine.Vector3(unityPosition.x, -unityPosition.z, unityPosition.y);
    }

    private UnityEngine.Quaternion UnityToRosRotation(UnityEngine.Quaternion unityRotation)
    {
        // Convert Unity's right-handed to ROS's left-handed coordinate system
        return new UnityEngine.Quaternion(-unityRotation.x, unityRotation.z, -unityRotation.y, unityRotation.w);
    }

    private void Start()
    {
        rosSocket = new RosSocket(new RosSharp.RosBridgeClient.Protocols.WebSocketNetProtocol(rosBridgeServerUrl));
        rosSocket.Subscribe<Float64MultiArray>(dogCommandTopic, DogCommandCallback);
    }

    private void Update()
    {
        // Publish drone pose
        PublishPoseStamped(dronePoseTopic, transform);

        // Publish dog pose
        GameObject dog = GameObject.Find("Dog");
        if (dog != null)
        {
            PublishPoseStamped(dogPoseTopic, dog.transform);
        }

        // Publish sheep poses (using PoseArray for multiple sheep)
        PublishSheepPoses();
    }

    private void PublishPoseStamped(string topic, UnityEngine.Transform transform)
    {
        PoseStamped poseStamped = new PoseStamped
        {
            header = new Header
            {
                frame_id = "map",
                stamp = new RosSharp.RosBridgeClient.MessageTypes.Std.Time()
            },
            pose = new RosSharp.RosBridgeClient.MessageTypes.Geometry.Pose
            {
                position = new Point
                {
                    x = UnityToRosPosition(transform.position).x,
                    y = UnityToRosPosition(transform.position).y,
                    z = UnityToRosPosition(transform.position).z
                },
                orientation = new RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion
                {
                    x = UnityToRosRotation(transform.rotation).x,
                    y = UnityToRosRotation(transform.rotation).y,
                    z = UnityToRosRotation(transform.rotation).z,
                    w = UnityToRosRotation(transform.rotation).w
                }
            }
        };

        rosSocket.Publish(topic, poseStamped);
    }

    private void PublishSheepPoses()
    {
        List<GameObject> sheepList = new List<GameObject>(GameObject.FindGameObjectsWithTag("Sheep"));

        PoseArray sheepPoseArray = new PoseArray
        {
            header = new Header
            {
                frame_id = "map",
                stamp = new RosSharp.RosBridgeClient.MessageTypes.Std.Time()
            },
            poses = new RosSharp.RosBridgeClient.MessageTypes.Geometry.Pose[sheepList.Count]
        };

        for (int i = 0; i < sheepList.Count; i++)
        {
            GameObject sheep = sheepList[i];
            sheepPoseArray.poses[i] = new RosSharp.RosBridgeClient.MessageTypes.Geometry.Pose
            {
                position = new Point
                {
                    x = UnityToRosPosition(sheep.transform.position).x,
                    y = UnityToRosPosition(sheep.transform.position).y,
                    z = UnityToRosPosition(sheep.transform.position).z
                },
                orientation = new RosSharp.RosBridgeClient.MessageTypes.Geometry.Quaternion
                {
                    x = UnityToRosRotation(sheep.transform.rotation).x,
                    y = UnityToRosRotation(sheep.transform.rotation).y,
                    z = UnityToRosRotation(sheep.transform.rotation).z,
                    w = UnityToRosRotation(sheep.transform.rotation).w
                }
            };
        }

        rosSocket.Publish(sheepPosesTopic, sheepPoseArray);
    }

    private void DogCommandCallback(Float64MultiArray command)
    {
        if (command.data.Length >= 2)
        {
            float targetX = (float)command.data[0];
            float targetY = (float)command.data[1];
            Debug.Log($"Received dog command: Move to ({targetX}, {targetY})");

            // TODO: Implement dog movement logic
        }
    }

    private void OnDestroy()
    {
        if (rosSocket != null)
        {
            rosSocket.Close();
        }
    }
}
