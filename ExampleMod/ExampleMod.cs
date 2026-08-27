using PunchLoader;
using UnityEngine;

namespace ExampleMod
{
    public class Plugin : IModPlugin
    {
        private BeginGUIHandler _beginGUIHandler;
        private bool _menuLogged;

        public string GetId() { return "ExampleMod"; }
        public string GetName() { return "Example Mod"; }
        public string GetVersion() { return "1.0.0"; }

        public void OnLoad()
        {
            _beginGUIHandler = OnBeginGUI;
            HookManager.Register(_beginGUIHandler);
            Debug.Log("[ExampleMod] Loaded.");
        }

        public void OnUnload()
        {
            HookManager.Unregister(_beginGUIHandler);
            _beginGUIHandler = null;
            Debug.Log("[ExampleMod] Unloaded.");
        }

        private void OnBeginGUI(MonoBehaviour menu)
        {
            // Demonstrate a working hook once without changing the game UI.
            if (_menuLogged || menu == null) return;

            _menuLogged = true;
            Debug.Log("[ExampleMod] BeginGUI hook: " + menu.GetType().FullName);
        }
    }
}
