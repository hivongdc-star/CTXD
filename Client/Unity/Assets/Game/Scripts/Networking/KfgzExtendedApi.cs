using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public sealed class KfgzExtendedApi
    {
        readonly ApiClient _api;
        public KfgzExtendedApi(ApiClient api) { _api = api; }

        public Task<KfgzBattleResourceView> GetResourcesAsync() => SendAsync<KfgzBattleResourceView>("GET", "/api/kfgz/resources", null);
        public Task<KfgzMubingResult> StartMubingAsync(int generalId) => SendAsync<KfgzMubingResult>("POST", "/api/kfgz/generals/" + generalId + "/mubing", null);
        public Task<KfgzFastRecruitResult> FastRecruitAsync(int generalId) => SendAsync<KfgzFastRecruitResult>("POST", "/api/kfgz/generals/" + generalId + "/fast-recruit", null);
        public Task<KfgzCallGeneralInfo> GetCallGeneralsAsync(int cityId) => SendAsync<KfgzCallGeneralInfo>("GET", "/api/kfgz/world/" + cityId + "/call-generals", null);
        public Task<KfgzCallGeneralResult> CallGeneralsAsync(int cityId, int[] generalIds) => SendAsync<KfgzCallGeneralResult>("POST", "/api/kfgz/world/" + cityId + "/call-generals", new KfgzCallGeneralRequest { generalIds = generalIds });
        public Task<KfgzReinforcementResult> ReinforceAsync(long battleId, int[] generalIds) => SendAsync<KfgzReinforcementResult>("POST", "/api/kfgz/battles/" + battleId + "/reinforce", new KfgzReinforcementRequest { generalIds = generalIds });
        public Task<KfgzPhantomResult> CreatePhantomAsync(long battleId) => SendAsync<KfgzPhantomResult>("POST", "/api/battles/" + battleId + "/phantom", new KfgzPhantomRequest { requestKey = Guid.NewGuid().ToString() });
        public Task<KfgzRushResult> RushAsync(long battleId, int[] generalIds, int cityId) => SendAsync<KfgzRushResult>("POST", "/api/battles/" + battleId + "/rush", new KfgzRushRequest { generalIds = generalIds, cityId = cityId });

        public Task<KfgzRewardView> GetRoundRewardAsync(long roundId) => SendAsync<KfgzRewardView>("GET", "/api/kfgz/rewards/round/" + roundId, null);
        public Task<KfgzRewardClaimResult> ClaimRoundRewardAsync(long roundId) => SendAsync<KfgzRewardClaimResult>("POST", "/api/kfgz/rewards/round/" + roundId + "/claim", null);
        public Task<KfgzEndRewardView> GetEndRewardAsync() => SendAsync<KfgzEndRewardView>("GET", "/api/kfgz/rewards/end", null);
        public Task<KfgzRewardClaimResult> ClaimEndRewardAsync(int slot) => SendAsync<KfgzRewardClaimResult>("POST", "/api/kfgz/rewards/end/" + slot + "/claim", null);
        public Task<KfgzTitlesResponse> GetTitlesAsync() => SendAsync<KfgzTitlesResponse>("GET", "/api/kfgz/titles", null);

        async Task<T> SendAsync<T>(string method, string path, object body)
        {
            using var req = new UnityWebRequest(_api.BaseUrl.TrimEnd('/') + path, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                var json = JsonUtility.ToJson(body);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(_api.Token)) req.SetRequestHeader("Authorization", "Bearer " + _api.Token);
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
            {
                var error = TryParse<ApiError>(req.downloadHandler.text);
                throw new ApiException(error != null && !string.IsNullOrEmpty(error.code) ? error.code : "NETWORK",
                    error != null && !string.IsNullOrEmpty(error.message) ? error.message : req.error);
            }
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }

        static T TryParse<T>(string json) where T : class
        {
            try { return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }
    }
}
