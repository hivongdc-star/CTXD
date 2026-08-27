using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace CTXD.Client.Networking
{
    public static class ApiClientKfzbFeastPublicExtensions
    {
        public static async Task<KfzbFeastPublicInfoView> GetKfzbFeastInfoAsync(this ApiClient api)
        {
            if (api == null) throw new ArgumentNullException(nameof(api));
            using var req = UnityWebRequest.Get(api.BaseUrl.TrimEnd('/') + "/api/kfzb/feast/info");
            req.downloadHandler = new DownloadHandlerBuffer();
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

            return JsonUtility.FromJson<KfzbFeastPublicInfoView>(req.downloadHandler.text);
        }
    }
}
