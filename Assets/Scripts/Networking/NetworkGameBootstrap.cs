using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace TwoVsTwoShooter.Networking
{
    public sealed class NetworkGameBootstrap : MonoBehaviour
    {
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        private NetworkManager networkManager;
        private UnityTransport transport;

        private void Awake()
        {
            networkManager = NetworkManager.Singleton;

            if (networkManager == null)
            {
                GameObject networkObject = new GameObject("NetworkManager");
                networkManager = networkObject.AddComponent<NetworkManager>();
                transport = networkObject.AddComponent<UnityTransport>();
                networkManager.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    PlayerPrefab = null
                };

                DontDestroyOnLoad(networkObject);
            }
            else
            {
                transport = networkManager.GetComponent<UnityTransport>();
            }

            EnsureTransportIsAssigned();
        }

        private void OnGUI()
        {
            const int width = 220;
            const int height = 36;
            GUILayout.BeginArea(new Rect(16, 16, width, 260), GUI.skin.box);

            GUILayout.Label("2v2 Shooter Network");
            address = GUILayout.TextField(address);

            string portText = GUILayout.TextField(port.ToString());
            if (ushort.TryParse(portText, out ushort parsedPort))
            {
                port = parsedPort;
            }

            bool isRunning = networkManager != null && networkManager.IsListening;
            GUI.enabled = !isRunning;

            if (GUILayout.Button("Host", GUILayout.Height(height)))
            {
                ConfigureTransport();
                networkManager.StartHost();
            }

            if (GUILayout.Button("Client", GUILayout.Height(height)))
            {
                ConfigureTransport();
                networkManager.StartClient();
            }

            if (GUILayout.Button("Server", GUILayout.Height(height)))
            {
                ConfigureTransport();
                networkManager.StartServer();
            }

            GUI.enabled = isRunning;

            if (GUILayout.Button("Shutdown", GUILayout.Height(height)))
            {
                networkManager.Shutdown();
            }

            GUI.enabled = true;
            GUILayout.Label(GetStatusText());
            GUILayout.EndArea();
        }

        private void ConfigureTransport()
        {
            EnsureTransportIsAssigned();

            transport.SetConnectionData(address, port);
        }

        private void EnsureTransportIsAssigned()
        {
            if (networkManager == null)
            {
                return;
            }

            if (networkManager.NetworkConfig == null)
            {
                networkManager.NetworkConfig = new NetworkConfig();
            }

            if (transport == null)
            {
                transport = networkManager.GetComponent<UnityTransport>();
            }

            if (transport == null)
            {
                transport = networkManager.gameObject.AddComponent<UnityTransport>();
            }

            networkManager.NetworkConfig.NetworkTransport = transport;
        }

        private string GetStatusText()
        {
            if (networkManager == null || !networkManager.IsListening)
            {
                return "Offline";
            }

            if (networkManager.IsHost)
            {
                return "Host";
            }

            if (networkManager.IsServer)
            {
                return "Server";
            }

            return networkManager.IsClient ? "Client" : "Offline";
        }
    }
}
