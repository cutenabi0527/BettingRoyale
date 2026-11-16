using System.Collections.Generic;
using BettingRoyale.Data.Online.Lobby;
namespace BettingRoyale.Data.Combat
{
    /// <summary>실제 참여자 이름과 현재 인원. 개별 적 위치를 UI 계약에 공개하지 않는다.</summary>
    public static class CombatHudRoster
    {
        private static readonly Dictionary<CombatantId,string> names=new();
        private static readonly Dictionary<int,Presentation> playerPresentations=new();
        public readonly struct Presentation
        {
            public Presentation(string portraitId, LobbyWeaponChoice weapon) { PortraitId=portraitId; Weapon=weapon; }
            public string PortraitId { get; }
            public LobbyWeaponChoice Weapon { get; }
        }
        public static int Alive { get; set; }
        public static int Total { get; set; }
        public static void SetName(CombatantId id,string value) { if (!string.IsNullOrWhiteSpace(value)) names[id]=value; }
        public static string Name(CombatantId id,string fallback) => names.TryGetValue(id,out var value) ? value : fallback;
        public static void SetPlayerPresentation(int playerId, string portraitId, LobbyWeaponChoice weapon)
        {
            if (playerId > 0) playerPresentations[playerId]=new Presentation(portraitId,weapon);
        }
        public static bool TryGetPlayerPresentation(int playerId, out Presentation value) => playerPresentations.TryGetValue(playerId,out value);
        public static void Reset() { names.Clear(); playerPresentations.Clear(); Alive=Total=0; }
    }
}
