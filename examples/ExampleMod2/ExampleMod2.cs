using System;
using PunchLoader;
using UnityEngine;

public class ExampleMod2Plugin : IModPlugin
{
    public string GetId() { return "ExampleMod2"; }
    public string GetName() { return "Example Mod2"; }
    public string GetVersion() { return "2.0.0"; }
    public void OnLoad()
    {
        Debug.Log("[ExampleMod2] Hello from ExampleMod2!");
    }
    public void OnUnload()
    {
        Debug.Log("[ExampleMod2] Goodbye!");
    }
}
