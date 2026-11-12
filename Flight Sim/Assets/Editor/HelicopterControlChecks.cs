using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Exercises real UDP sockets and Unity JSON parsing without entering Play mode.</summary>
[InitializeOnLoad]
public static class HelicopterControlChecks
{
    const string Request = "Library/HelicopterControlChecks.request";
    const string Result = "Library/HelicopterControlChecks.result.txt";
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    static HelicopterControlChecks() => EditorApplication.update += CheckRequest;

    static void CheckRequest()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;
        File.Delete(Request);
        try { File.WriteAllText(Result, RunChecks()); }
        catch (Exception exception) { File.WriteAllText(Result, exception.ToString()); Debug.LogException(exception); }
    }

    [MenuItem("Tools/Flight Sim/Validate Local Controls")]
    public static void Validate() => Debug.Log(RunChecks());

    static string RunChecks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run these checks outside Play mode.");

        var host = new GameObject("Temporary helicopter input checks") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        var receiver = host.AddComponent<HelicopterControlReceiver>();
        host.SetActive(true);
        using var sender = new UdpClient();
        try
        {
            // These MonoBehaviour lifecycle calls are explicit because checks run in Edit mode.
            Invoke(receiver, "OnDisable");
            Invoke(receiver, "OnEnable");
            Require(typeof(HelicopterControlReceiver).GetField("m_Client", PrivateInstance).GetValue(receiver) != null,
                "The default bridge port could not be bound on this computer.");
            Invoke(receiver, "OnDisable");
            // After checking the real bridge port, isolate the packet tests on an OS-selected port.
            typeof(HelicopterControlReceiver).GetField("m_Port", PrivateInstance).SetValue(receiver, 0);
            Invoke(receiver, "OnEnable");
            var client = (UdpClient)typeof(HelicopterControlReceiver).GetField("m_Client", PrivateInstance).GetValue(receiver);
            var endpoint = (IPEndPoint)client.Client.LocalEndPoint;
            Require(!receiver.TryRead(out var initial) && initial.yaw == 0f, "No input must be neutral.");

            Send(sender, endpoint, "{\"cyclicX\":0.25,\"cyclicY\":-0.5,\"collective\":0.75,\"yaw\":-1,\"sequence\":12}");
            WaitForPacket(client);
            Require(receiver.TryRead(out var controls), "Valid packet was not accepted.");
            Require(controls.cyclicX == 0.25f && controls.cyclicY == -0.5f
                && controls.collective == 0.75f && controls.yaw == -1f, "Axis mapping changed in transit.");

            Send(sender, endpoint, "{\"cyclicX\":9,\"cyclicY\":-9,\"collective\":4,\"yaw\":-4}");
            WaitForPacket(client);
            receiver.TryRead(out controls);
            Require(controls.cyclicX == 1f && controls.cyclicY == -1f
                && controls.collective == 1f && controls.yaw == -1f, "Axes must be clamped.");

            foreach (var invalid in new[] { "invalid JSON", "{}", "{\"yaw\":1}",
                "{\"cyclicX\":1e99,\"cyclicY\":0,\"collective\":0,\"yaw\":0}" })
            {
                typeof(HelicopterControlReceiver).GetField("m_LastReceived", PrivateInstance)
                    .SetValue(receiver, Time.realtimeSinceStartup - 2f);
                Send(sender, endpoint, invalid);
                WaitForPacket(client);
                Require(!receiver.TryRead(out controls) && controls.cyclicX == 0f && controls.yaw == 0f,
                    "Invalid packets must not refresh timed-out input: " + invalid);
            }

            Send(sender, endpoint, "{\"cyclicX\":0,\"cyclicY\":0,\"collective\":0,\"yaw\":1}");
            WaitForPacket(client);
            Require(receiver.TryRead(out controls) && controls.yaw == 1f, "Receiver did not recover after timeout.");
            Invoke(receiver, "OnDisable");
            Require(!receiver.TryRead(out controls) && controls.yaw == 0f, "Disabled receiver must return neutral.");
            Invoke(receiver, "OnEnable");
            Require(!receiver.TryRead(out controls), "Reopened receiver must not retain old input.");
            return "PASS: default bridge port binding, UDP delivery, all four axes, clamping, malformed/incomplete/non-finite rejection, timeout, recovery, socket close/reopen.";
        }
        finally
        {
            Invoke(receiver, "OnDisable");
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static void Send(UdpClient sender, IPEndPoint endpoint, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        sender.Send(bytes, bytes.Length, endpoint);
    }

    static void WaitForPacket(UdpClient client)
    {
        Require(client.Client.Poll(100000, SelectMode.SelectRead), "UDP packet did not arrive within 100 ms.");
    }

    static void Invoke(HelicopterControlReceiver receiver, string method) =>
        typeof(HelicopterControlReceiver).GetMethod(method, PrivateInstance).Invoke(receiver, null);

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
