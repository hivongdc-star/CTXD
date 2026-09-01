using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CTXD.Client.Features.Nation;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Mail
{
    public sealed class MailPanel : MonoBehaviour
    {
        const string Root = "LegacyVisual/Mail/";
        ApiClient _api; Action<string> _status; RectTransform _window; MailPage _page; MailView _read;
        readonly HashSet<long> _selected = new HashSet<long>(); int _requestedPage; bool _busy;
        public static MailPanel Open(RectTransform host, ApiClient api, Action<string> status)
        {
            var go = new GameObject("MailPanel"); go.transform.SetParent(host, false);
            var p = go.AddComponent<MailPanel>(); p._api = api; p._status = status; p.Build(); _ = p.Refresh(); return p;
        }
        void Build()
        {
            var overlay = W7LegacyUi.Overlay(transform, "MailLegacyOverlay");
            _window = W7LegacyUi.Window(overlay, W7LegacyUi.Common + "Window3", 309, 191, 662, 385);
            W7LegacyUi.Close(_window, 635, 6, () => Destroy(gameObject));
        }
        async Task Refresh()
        {
            try { _page = await _api.GetMailAsync(_requestedPage, false); Draw(); }
            catch (Exception ex) { _status(ex.Message); }
        }
        void Draw()
        {
            for (var i = _window.childCount - 1; i >= 0; i--) { var c = _window.GetChild(i); if (c.name != "W7Close") Destroy(c.gameObject); }
            W7LegacyUi.Image(_window, Root + "mail_list", 0, 0, 220, 360, true);
            W7LegacyUi.Image(_window, Root + "read_bg", 222, 105, 400, 235, true);
            var rows = _page?.items ?? Array.Empty<MailView>();
            for (var i = 0; i < 6; i++)
            {
                var y = 80 + i * 46; if (i >= rows.Length) continue; var mail = rows[i];
                W7LegacyUi.Image(_window, Root + "row_bg", 12, y, 198, 46);
                W7LegacyUi.Toggle(_window, _selected.Contains(mail.id), 21, y + 8, true, on => { if (on) _selected.Add(mail.id); else _selected.Remove(mail.id); });
                W7LegacyUi.Text(_window, mail.sender, 39, y + 9, 110, 20, 12, TextAnchor.MiddleLeft, W7LegacyUi.Gold);
                W7LegacyUi.Text(_window, ShortTime(mail.createdAt), 146, y + 9, 68, 20, 11, TextAnchor.MiddleRight, W7LegacyUi.Muted);
                W7LegacyUi.Text(_window, mail.title, 42, y + 29, 170, 17, 11, TextAnchor.MiddleLeft, mail.isRead ? W7LegacyUi.Muted : W7LegacyUi.Gold);
                W7LegacyUi.Hit(_window, 39, y, 173, 46, async () => await OpenMail(mail));
            }
            var pages = Math.Max(1, _page?.totalPages ?? 1);
            W7LegacyUi.Pager(_window, true, 152, 373, _requestedPage, pages,
                () => { if (_requestedPage > 0) { _requestedPage--; _selected.Clear(); _ = Refresh(); } },
                () => { if (_requestedPage + 1 < pages) { _requestedPage++; _selected.Clear(); _ = Refresh(); } });
            W7LegacyUi.Button22(_window, "Xóa", 14, 368, 50, 28, async () => await DeleteSelected());
            W7LegacyUi.Button22(_window, "Chọn hết", 62, 368, 50, 28, () => { foreach (var m in rows) _selected.Add(m.id); Draw(); });
            var write = W7LegacyUi.Button23(_window, "Viết", 490, 368, 78, 34, () => { }); write.interactable = false;
            var reply = W7LegacyUi.Button23(_window, "Trả lời", 572, 368, 78, 34, () => { }); reply.interactable = false;
            if (_read != null)
            {
                W7LegacyUi.Text(_window, _read.sender, 290, 64, 340, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Gold);
                W7LegacyUi.Text(_window, _read.title, 290, 93, 340, 20, 13, TextAnchor.MiddleLeft, W7LegacyUi.Gold);
                var body = W7LegacyUi.Text(_window, _read.body, 244, 130, 377, 220, 13, TextAnchor.UpperLeft, W7LegacyUi.Gold);
                body.horizontalOverflow = HorizontalWrapMode.Wrap; body.verticalOverflow = VerticalWrapMode.Truncate;
            }
        }
        async Task OpenMail(MailView mail)
        {
            _read = mail;
            if (!mail.isRead)
            {
                try { await _api.ReadMailAsync(mail.id); mail.isRead = true; }
                catch (Exception ex) { _status(ex.Message); }
            }
            Draw();
        }
        async Task DeleteSelected()
        {
            if (_busy || _selected.Count == 0) return; _busy = true;
            try
            {
                var ids = new List<long>(_selected); foreach (var id in ids) await _api.DeleteMailAsync(id);
                _selected.Clear(); if (_read != null && ids.Contains(_read.id)) _read = null; await Refresh();
            }
            catch (Exception ex) { _status(ex.Message); }
            finally { _busy = false; }
        }
        static string ShortTime(string value)
        {
            if (DateTimeOffset.TryParse(value, out var t)) return t.ToLocalTime().ToString("MM-dd HH:mm");
            return value ?? string.Empty;
        }
    }
}
