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
    private Vector3 UnityToRosPosition(Vector3 unityPosition)
    {
        // Convert Unity's right-handed to ROS's left-handed coordinate system
        return new Vector3(unityPosition.x, -unityPosition.z, unityPosition.y);
    }

    private Quaternion UnityToRosRotation(Quaternion unityRotation)
    {
        // Convert Unity's right-handed to ROS's left-handed coordinate system
        return new Quaternion(-unityRotation.x, unityRotation.z, -unityRotation.y, unityRotation.w);
    }

    private void Start()
    {
        rosSocket = new RosSocket(rosBridgeServerUrl);
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

        // Publish sheep poses
        PublishSheepPoses();
    }

    private void PublishPoseStamped(string topic, Transform transform)
    {
        PoseStamped poseStamped = new PoseStamped
        {
            header = new Header
            {
                frame_id = "map",
                stamp = new Time
                {
                    secs = (int)Time.time,
                    nsecs = (uint)((Time.time % 1) * 1e9)
                }
            },
            pose = new Pose
            {
                position = new Point
                {
                    x = UnityToRosPosition(transform.position).x,
                    y = UnityToRosPosition(transform.position).y,
                    z = UnityToRosPosition(transform.position).z
                },
                orientation = new Quaternion
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
        List<PoseStamped> sheepPoses = new List<PoseStamped>();

        foreach (GameObject sheep in sheepList)
        {
            PoseStamped poseStamped = new PoseStamped
            {
                header = new Header
                {
                    frame_id = "map",
                    stamp = new Time
                    {
                        secs = (int)Time.time,
                        nsecs = (uint)((Time.time % 1) * 1e9)
                    }
                },
                pose = new Pose
                {
                    position = new Point
                    {
                        x = UnityToRosPosition(sheep.transform.position).x,
                        y = UnityToRosPosition(sheep.transform.position).y,
                        z = UnityToRosPosition(sheep.transform.position).z
                    },
                    orientation = new Quaternion
                    {
                        x = UnityToRosRotation(sheep.transform.rotation).x,
                        y = UnityToRosRotation(sheep.transform.rotation).y,
                        z = UnityToRosRotation(sheep.transform.rotation).z,
                        w = UnityToRosRotation(sheep.transform.rotation).w
                    }
                }
            };

            sheepPoses.Add(poseStamped);
        }

        rosSocket.Publish(sheepPosesTopic, sheepPoses);
    }

    private void DogCommandCallback(Float64MultiArray command)
    {
        // Handle incoming dog command
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