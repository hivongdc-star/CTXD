using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public sealed class RankApi
    {
        readonly ApiClient _api;
        public RankApi(ApiClient api) { _api = api; }

        public async Task<LevelRankView> GetLevelRankAsync()
        {
            using var req = UnityWebRequest.Get(_api.BaseUrl.TrimEnd('/') + "/api/rank/1");
            if (!string.IsNullOrEmpty(_api.Token)) req.SetRequestHeader("Authorization", "Bearer " + _api.Token);
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
            {
                ApiError error = null;
                try { if (!string.IsNullOrWhiteSpace(req.downloadHandler.text)) error = JsonUtility.FromJson<ApiError>(req.downloadHandler.text); } catch { }
                throw new ApiException(error != null && !string.IsNullOrEmpty(error.code) ? error.code : "NETWORK",
                    error != null && !string.IsNullOrEmpty(error.message) ? error.message : req.error);
            }
            return JsonUtility.FromJson<LevelRankView>(req.downloadHandler.text);
        }
    }
}
