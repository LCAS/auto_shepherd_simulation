using NUnit.Framework;
using UnityEngine;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Geometry;
using RosSharp.RosBridgeClient.MessageTypes.Std;
using System.Collections.Generic;

[TestFixture]
public class TestRosInterface
{
    private RosInterface rosInterface;

    [SetUp]
    public void Setup()
    {
        rosInterface = new RosInterface();
    }

    [TearDown]
    public void TearDown()
    {
        rosInterface = null;
    }

    [Test]
    public void TestInitialization()
    {
        Assert.IsNotNull(rosInterface);
    }

    [Test]
    public void TestPublishDronePose()
    {
        // Mock the RosSocket and verify that Publish is called
        var mockRosSocket = new Mock<RosSocket>();
        rosInterface.rosSocket = mockRosSocket.Object;
        rosInterface.Update();
        mockRosSocket.Verify(socket => socket.Publish(It.IsAny<string>(), It.IsAny<Point>()), Times.Once);
    }

    [Test]
    public void TestPublishDogPose()
    {
        // Mock the RosSocket and verify that Publish is called
        var mockRosSocket = new Mock<RosSocket>();
        rosInterface.rosSocket = mockRosSocket.Object;
        rosInterface.Update();
        mockRosSocket.Verify(socket => socket.Publish(It.IsAny<string>(), It.IsAny<Point>()), Times.Once);
    }

    [Test]
    public void TestPublishSheepPoses()
    {
        // Mock the RosSocket and verify that Publish is called
        var mockRosSocket = new Mock<RosSocket>();
        rosInterface.rosSocket = mockRosSocket.Object;
        rosInterface.Update();
        mockRosSocket.Verify(socket => socket.Publish(It.IsAny<string>(), It.IsAny<List<Point>>()), Times.Once);
    }
} 