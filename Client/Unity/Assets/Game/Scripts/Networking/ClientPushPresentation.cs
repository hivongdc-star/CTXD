using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CTXD.Client.Core;
using CTXD.Client.Features.FirstPlayable;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Networking
{
    public sealed class ClientPushPresentation : MonoBehaviour
    {
        static ClientPushPresentation _instance;
        readonly ConcurrentQueue<string> _messages = new();
        ApiClient _api;
        Text _notice;
        bool _courtesyRefreshInFlight;
        bool _courtesyRefreshQueued;

        [Serializable]
        sealed class PushEnvelope
        {
            public string type;
            public PushPayload payload;
        }

        [Serializable]
        sealed class PushPayload
        {
            public string command;
            public string type;
            public string content;
            public bool liShangWangLai;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindFirstObjectByType<ClientPushPresentation>() == null)
                new GameObject("CTXD_ClientPushPresentation").AddComponent<ClientPushPresentation>();
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            RealtimeClient.MessageObserved -= OnMessageObserved;
            RealtimeClient.MessageObserved += OnMessageObserved;
        }

        void OnDestroy()
        {
            if (_instance != this) return;
            RealtimeClient.MessageObserved -= OnMessageObserved;
            _instance = null;
        }

        void OnMessageObserved(string message)
        {
            if (!string.IsNullOrEmpty(message)) _messages.Enqueue(message);
        }

        void Update()
        {
            while (_messages.TryDequeue(out var message)) Dispatch(message);
        }

        void Dispatch(string message)
        {
            PushEnvelope envelope;
            try { envelope = JsonUtility.FromJson<PushEnvelope>(message); }
            catch { return; }
            if (envelope == null || string.IsNullOrEmpty(envelope.type) || envelope.payload == null) return;

            if (envelope.type == "push@notice")
            {
                if (envelope.payload.command == "notice" && envelope.payload.type == "COUNTRY" && !string.IsNullOrEmpty(envelope.payload.content))
                    Present(ConvertLegacyRichText(envelope.payload.content));
                return;
            }

            if (envelope.type == "courtesy.updated" && envelope.payload.liShangWangLai)
                QueueCourtesyRefresh();
        }

        void QueueCourtesyRefresh()
        {
            if (_courtesyRefreshInFlight)
            {
                _courtesyRefreshQueued = true;
                return;
            }
            _ = RefreshCourtesyAsync();
        }

        async Task RefreshCourtesyAsync()
        {
            _courtesyRefreshInFlight = true;
            try
            {
                do
                {
                    _courtesyRefreshQueued = false;
                    var token = PlayerPrefs.GetString("ctxd.session", "");
                    if (string.IsNullOrEmpty(token)) return;

                    _api ??= new ApiClient();
                    _api.BaseUrl = ClientConfig.ServerUrl;
                    _api.Token = token;
                    var state = await _api.GetCourtesyAsync();
                    if (state == null || !state.open || !state.liShangWangLai) continue;

                    var pending = 0;
                    var events = state.events ?? Array.Empty<CourtesyEventView>();
                    for (var i = 0; i < events.Length; i++)
                        if (events[i] != null && events[i].state == 1) pending++;

                    Present(pending > 0
                        ? "Lễ Thượng Vãng Lai: " + pending + " việc chờ xử lý."
                        : "Lễ Thượng Vãng Lai đã cập nhật.");
                }
                while (_courtesyRefreshQueued);
            }
            catch (ApiException ex)
            {
                Debug.LogWarning("Courtesy refresh from realtime push failed: " + ex.Message);
            }
            finally
            {
                _courtesyRefreshInFlight = false;
            }
        }

        void Present(string value)
        {
            if (_notice == null)
            {
                var canvasObject = GameObject.Find("CTXD_LegacyCanvas");
                if (canvasObject == null) return;
                _notice = LegacyUiFactory.Label(canvasObject.transform, "", 16, TextAnchor.MiddleCenter,
                    new Color(1f, .86f, .45f), new Vector2(.18f, .86f), new Vector2(.82f, .91f));
                _notice.gameObject.name = "ServerPushNotice";
                _notice.raycastTarget = false;
                var outline = _notice.gameObject.AddComponent<Outline>();
                outline.effectColor = Color.black;
                outline.effectDistance = new Vector2(1, -1);
            }
            _notice.text = value;
            _notice.transform.SetAsLastSibling();
        }

        static string ConvertLegacyRichText(string value) => value
            .Replace("<font color=\"#00FF00\">", "<color=#00FF00>")
            .Replace("</font>", "</color>");
    }
}
