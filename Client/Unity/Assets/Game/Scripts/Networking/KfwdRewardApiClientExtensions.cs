using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public static class KfwdRewardApiClientExtensions
    {
        public static Task<KfwdRewardView> GetKfwdRewardsAsync(this ApiClient api) =>
            SendAsync<KfwdRewardView>(api, "GET", "/api/kfwd/rewards", null);

        public static Task<KfwdGeneralTreasureView> ClaimKfwdTreasureAsync(this ApiClient api) =>
            SendAsync<KfwdGeneralTreasureView>(api, "POST", "/api/kfwd/treasure/claim", null);

        static async Task<T> SendAsync<T>(ApiClient api, string method, string path, object body)
        {
            using var req = new UnityWebRequest(api.BaseUrl.TrimEnd('/') + path, method);
            req.downloadHandler = new DownloadHandlerBuffer();
            if (body != null)
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
                req.SetRequestHeader("Content-Type", "application/json");
            }
            if (!string.IsNullOrEmpty(api.Token))
                req.SetRequestHeader("Authorization", "Bearer " + api.Token);

            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();

            if (req.result != UnityWebRequest.Result.Success)
            {
                ApiError error = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(req.downloadHandler.text))
                        error = JsonUtility.FromJson<ApiError>(req.downloadHandler.text);
                }
                catch { }
                throw new ApiException(error != null && !string.IsNullOrEmpty(error.code) ? error.code : "NETWORK",
                    error != null && !string.IsNullOrEmpty(error.message) ? error.message : req.error);
            }
            return JsonUtility.FromJson<T>(req.downloadHandler.text);
        }
    }
}
