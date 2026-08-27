using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public static class ApiClientKfzbRewardExtensions
    {
        public static Task<KfzbRewardView> GetKfzbRewardAsync(this ApiClient api) =>
            SendAsync<KfzbRewardView>(api, "GET", "/api/kfzb/reward", null);

        public static Task<KfzbRewardClaimResult> ClaimKfzbRewardAsync(this ApiClient api) =>
            SendAsync<KfzbRewardClaimResult>(api, "POST", "/api/kfzb/reward/claim", null);

        public static Task<GeneralTreasureListResponse> GetGeneralTreasuresAsync(this ApiClient api) =>
            SendAsync<GeneralTreasureListResponse>(api, "GET", "/api/general-treasures", null);

        public static Task<GeneralTreasureView> EquipGeneralTreasureAsync(this ApiClient api, long instanceId, int generalId) =>
            SendAsync<GeneralTreasureView>(api, "POST", "/api/general-treasures/" + instanceId + "/equip",
                new GeneralTreasureEquipRequest { generalId = generalId });

        public static Task<GeneralTreasureView> UnequipGeneralTreasureAsync(this ApiClient api, long instanceId) =>
            SendAsync<GeneralTreasureView>(api, "POST", "/api/general-treasures/" + instanceId + "/unequip", null);

        static async Task<T> SendAsync<T>(ApiClient api, string method, string path, object body)
        {
            using var req = new UnityWebRequest(api.BaseUrl.TrimEnd('/') + path, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                var json = JsonUtility.ToJson(body);
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(api.Token)) req.SetRequestHeader("Authorization", "Bearer " + api.Token);

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
