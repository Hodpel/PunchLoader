using System;
using PunchLoader;
using UnityEngine;

public class ExamplePlugin : IModPlugin
{
    public string GetId() { return "ExampleMod"; }
    public string GetName() { return "Example Mod"; }
    public string GetVersion() { return "1.0.0"; }
    public void OnLoad()
    {
        Debug.Log("[ExampleMod] Hello from ExampleMod!");
    }
    public void OnUnload()
    {
        Debug.Log("[ExampleMod] Goodbye!");
    }
}
