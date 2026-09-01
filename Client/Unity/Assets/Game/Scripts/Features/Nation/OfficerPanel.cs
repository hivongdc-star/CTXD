using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Nation
{
    public sealed class OfficerPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Officer/";
        ApiClient _api; Action<string> _status; RectTransform _panel; OfficeView[] _offices; int _index, _memberPage; bool _busy;
        public static OfficerPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("OfficerPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<OfficerPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "OfficerLegacyScene");
            W7LegacyUi.Image(overlay, Root + "scene", 280, 84, 1000, 600);
            _panel = W7LegacyUi.Window(overlay, Root + "official_bg", 169, 94, 662, 412);
            W7LegacyUi.Close(_panel, 636, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _offices = await _api.GetOfficesAsync(); if (_offices == null) _offices = Array.Empty<OfficeView>(); _index = Mathf.Clamp(_index, 0, Math.Max(0, _offices.Length - 1)); Draw(); }
            catch (Exception ex) { _status(ex.Message); Destroy(gameObject); }
        }
        void Draw()
        {
            for (var i = _panel.childCount - 1; i >= 0; i--) { var c = _panel.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            if (_offices.Length == 0) return;
            for (var i = 0; i < Math.Min(5, _offices.Length); i++)
            {
                var idx = i;
                W7LegacyUi.Button5(_panel, _offices[i].leaderTitle ?? ("#" + _offices[i].buildingId), 25 + i * 75, 6, 74, 25, () => { _index = idx; _memberPage = 0; Draw(); });
            }
            var office = _offices[_index];
            var officialFile = Mathf.Clamp(_index + 1, 1, 5).ToString();
            W7LegacyUi.Image(_panel, Root + "Official/" + officialFile, 60, 106, 150, 150, true);
            W7LegacyUi.Text(_panel, office.leaderTitle ?? string.Empty, 310, 50, 180, 20, 14, TextAnchor.MiddleCenter, new Color(.32f, .27f, .22f));
            var members = office.members ?? Array.Empty<OfficeMemberView>();
            var pageSize = 10; var pages = Math.Max(1, (members.Length + pageSize - 1) / pageSize); _memberPage = Mathf.Clamp(_memberPage, 0, pages - 1);
            for (var r = 0; r < pageSize; r++)
            {
                var at = _memberPage * pageSize + r; if (at >= members.Length) break; var m = members[at]; var y = 115 + r * 23;
                W7LegacyUi.Text(_panel, m.isLeader ? office.leaderTitle : office.memberTitle, 237, y, 60, 20, 12, TextAnchor.MiddleCenter);
                W7LegacyUi.Text(_panel, m.name, 307, y, 100, 20, 12, TextAnchor.MiddleCenter);
                W7LegacyUi.Text(_panel, m.level.ToString(), 420, y, 30, 20, 12, TextAnchor.MiddleCenter);
                W7LegacyUi.Text(_panel, m.isLeader ? office.leaderTitle : office.memberTitle, 467, y, 70, 20, 12, TextAnchor.MiddleCenter);
            }
            W7LegacyUi.Pager(_panel, false, 420, 358, _memberPage, pages, () => { if (_memberPage > 0) { _memberPage--; Draw(); } }, () => { if (_memberPage + 1 < pages) { _memberPage++; Draw(); } });
            var attack = W7LegacyUi.Button5(_panel, "Tranh chức", 84, 337, 72, 37, () => { });
            attack.interactable = false;
            var canApply = !members.Any(m => m.isLeader && m.playerId == office.ownerPlayerId);
            if (canApply) W7LegacyUi.Button5(_panel, "Xin vào", 10, 33, 72, 25, async () => await Apply(office.buildingId));
        }
        async Task Apply(int buildingId)
        {
            if (_busy) return; _busy = true;
            try { await _api.ApplyOfficeAsync(buildingId); await Refresh(); }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
    }
}
