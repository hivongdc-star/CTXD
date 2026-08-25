using UnityEngine;

namespace CTXD.Client.Core
{
    public static class ClientConfig
    {
        const string ServerKey = "ctxd.server.url";
        public static string ServerUrl
        {
            get => PlayerPrefs.GetString(ServerKey, "http://127.0.0.1:5080");
            set { PlayerPrefs.SetString(ServerKey, value); PlayerPrefs.Save(); }
        }
    }
}
