using System;
using System.Linq;
using System.Threading.Tasks;
using CTXD.Client.Features.FirstPlayable;
using CTXD.Client.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace CTXD.Client.Features.Battle
{
    public sealed class BattlePanel : MonoBehaviour
    {
        static BattlePanel _open;
        ApiClient _api; Action<string> _status; RectTransform _window; long _battleId; BattleView _battle; bool _busy;

        public static BattlePanel Open(RectTransform host, ApiClient api, Action<string> status, long battleId)
        {
            var go=new GameObject("BattlePanel");go.transform.SetParent(host,false);var panel=go.AddComponent<BattlePanel>();
            panel._api=api;panel._status=status;panel._battleId=battleId;_open=panel;panel.Build();_=panel.RefreshAsync();return panel;
        }
        public static void RefreshOpenFromPush(){if(_open!=null&&!_open._busy)_=_open.RefreshAsync();}
        void OnDestroy(){if(_open==this)_open=null;}
        void Build(){var blocker=LegacyUiFactory.Panel(transform,"BattleBlocker",Vector2.zero,Vector2.one,new Color(0,0,0,.9f));_window=LegacyUiFactory.PixelPanel(blocker,"BattleWindow",125,70,1030,580,new Color(.045f,.03f,.02f,1));LegacyUiFactory.PixelLabel(_window,"CHIẾN TRƯỜNG",25,TextAnchor.MiddleCenter,new Color(1f,.78f,.25f),370,12,290,38);}
        async Task RefreshAsync(){try{_battle=await _api.GetBattleAsync(_battleId);Draw();}catch(Exception ex){_status(ex.Message);}}
        void Draw()
        {
            if(_window==null||_battle==null)return;LegacyUiFactory.DestroyChildren(_window);
            LegacyUiFactory.PixelLabel(_window,"CHIẾN TRƯỜNG · Thành #"+_battle.cityId,23,TextAnchor.MiddleCenter,new Color(1f,.78f,.25f),300,10,430,38);
            LegacyUiFactory.PixelButton(_window,"Đóng",940,12,72,28,()=>Destroy(gameObject));
            DrawSide(_battle.attackers??Array.Empty<BattleUnitView>(),true);DrawSide(_battle.defenders??Array.Empty<BattleUnitView>(),false);
            var state=_battle.status==0?"Round "+_battle.roundNo:_battle.winnerSide==1?"CÔNG THẮNG":"THỦ THẮNG";
            LegacyUiFactory.PixelLabel(_window,state,20,TextAnchor.MiddleCenter,Color.white,410,420,210,36);
            if(_battle.status==0){LegacyUiFactory.PixelButton(_window,"Tiến hành round",410,470,210,45,async()=>await AdvanceAsync());LegacyUiFactory.PixelButton(_window,"Deploy Team",810,470,150,35,async()=>await DeployTeamAsync());}
            if(_battle.status==0){var own=(_battle.attackers??Array.Empty<BattleUnitView>()).FirstOrDefault(x=>!x.dead&&!x.isNpc);if(own!=null){if(own.tacticAvailable)LegacyUiFactory.PixelButton(_window,"Tactic #"+own.tacticId,345,440,130,28,async()=>await ChooseAsync(own,1,0));var choices=own.allowedStrategyIds??Array.Empty<int>();for(var i=0;i<Math.Min(3,choices.Length);i++){var strategy=choices[i];LegacyUiFactory.PixelButton(_window,"Strategy "+strategy,485+i*135,440,128,28,async()=>await ChooseAsync(own,2,strategy));}}}
            var last=(_battle.rounds??Array.Empty<BattleRoundView>()).LastOrDefault();if(last!=null)LegacyUiFactory.PixelLabel(_window,"Sát thương công: "+last.attackerDamage+" · thủ: "+last.defenderDamage,15,TextAnchor.MiddleCenter,new Color(.9f,.8f,.65f),320,530,390,25);
        }
        void DrawSide(BattleUnitView[] units,bool attacker)
        {
            var x=attacker?25:705;var title=attacker?"PHE CÔNG":"PHE THỦ";LegacyUiFactory.PixelLabel(_window,title,18,TextAnchor.MiddleCenter,attacker?new Color(1f,.4f,.3f):new Color(.3f,.65f,1f),x,65,300,30);
            for(var i=0;i<Math.Min(6,units.Length);i++){var u=units[i];var y=105+i*50;LegacyUiFactory.PixelLabel(_window,u.name+(u.isNpc?" [NPC]":""),15,TextAnchor.MiddleLeft,u.dead?Color.gray:Color.white,x,y,190,22);var ratio=u.maxHp<=0?0f:Mathf.Clamp01((float)u.hp/u.maxHp);LegacyUiFactory.PixelPanel(_window,"HpBg",x,y+25,280,12,new Color(.18f,.08f,.05f));LegacyUiFactory.PixelPanel(_window,"Hp",x,y+25,280*ratio,12,attacker?new Color(.8f,.18f,.12f):new Color(.12f,.42f,.85f));}
        }
        async Task AdvanceAsync(){if(_busy)return;_busy=true;try{_battle=await _api.AdvanceBattleAsync(_battleId);Draw();_status(_battle.status==0?"Server đã xử lý round.":"Battle đã kết thúc; World đang cập nhật.");}catch(Exception ex){_status(ex.Message);}finally{_busy=false;}}
        async Task ChooseAsync(BattleUnitView unit,int action,int strategy){if(_busy)return;_busy=true;try{_battle=await _api.ChooseBattleActionAsync(_battleId,unit.generalId,action,strategy);Draw();_status(action==1?"Server đã chọn tactic.":"Server đã chọn strategy.");}catch(Exception ex){_status(ex.Message);}finally{_busy=false;}}
        async Task DeployTeamAsync(){if(_busy)return;_busy=true;try{var teams=await _api.GetTeamsAsync();var team=(teams.items??Array.Empty<TeamView>()).FirstOrDefault(x=>x.isOwner);if(team==null)throw new Exception("No owned team is ready.");var result=await _api.DeployTeamAsync(team.id,_battleId,team.members.Length,0);_battle=result.battle;Draw();_status("Deployed "+result.deployed+" team generals.");}catch(Exception ex){_status(ex.Message);}finally{_busy=false;}}
    }
}
